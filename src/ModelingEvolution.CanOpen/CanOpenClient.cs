using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModelingEvolution.CanOpen;

/// <summary>
/// Layer-1 CANopen client over a <see cref="CanInterface"/>: NMT control, expedited SDO,
/// TxPDO on-change subscription (with PDO-map routing + SDO-poll fallback), and a cyclic
/// RxPDO image. A single background receive loop demultiplexes by COB-ID.
///
/// Decoding/encoding of multi-byte values is little-endian = host order on the supported
/// targets (x64, ARM64). No vendor/device vocabulary lives here.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class CanOpenClient : IDisposable
{
    private readonly CanInterface _bus;
    private readonly int _node;
    private readonly ILogger _logger;

    private readonly uint _sdoResp;
    private readonly uint _bootup;
    private readonly uint _emcy;

    private readonly Lock _subGate = new();
    private readonly Dictionary<uint, List<Action<CanMessage>>> _subscribers = new();
    private readonly List<Timer> _timers = new();

    private readonly SemaphoreSlim _sdoLock = new(1, 1);
    private readonly Lock _sdoGate = new();
    private TaskCompletionSource<CanMessage>? _pendingSdo;

    private readonly CancellationTokenSource _cts = new();
    private Thread? _rxThread;
    private PdoMap? _pdoMap;
    private int _started;

    /// <summary>Raised when the node sends its bootup message (heartbeat COB-ID, single 0x00 byte).</summary>
    public event Action? BootupReceived;

    /// <summary>Raised on an EMCY frame; argument is the 16-bit error code (bytes 0-1, little-endian).</summary>
    public event Action<ushort>? EmcyReceived;

    public int NodeId => _node;

    public CanOpenClient(CanInterface bus, byte nodeId, ILogger<CanOpenClient>? logger = null)
    {
        _bus = bus;
        _node = nodeId;
        _logger = logger ?? NullLogger<CanOpenClient>.Instance;
        _sdoResp = CanOpenDefs.SdoResponse(nodeId);
        _bootup = CanOpenDefs.Bootup(nodeId);
        _emcy = CanOpenDefs.Emcy(nodeId);
    }

    /// <summary>Starts the background receive loop. Idempotent.</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
            return;
        _rxThread = new Thread(ReceiveLoop) { IsBackground = true, Name = $"canopen-rx-n{_node}" };
        _rxThread.Start();
    }

    // ---- NMT ----------------------------------------------------------------
    public void Nmt(byte command) => _bus.Send(CanOpenDefs.CobNmt, CanOpenDefs.NmtFrame(command, _node));
    public void NmtStart() => Nmt(CanOpenDefs.NmtStart);
    public void NmtPreOperational() => Nmt(CanOpenDefs.NmtPreOperational);

    // ---- SDO ----------------------------------------------------------------
    /// <summary>Expedited SDO upload (read). Returns the data bytes, or null on timeout/abort.</summary>
    public async Task<byte[]?> SdoUploadAsync(ObjectAddress a, int timeoutMs = 200, CancellationToken ct = default)
    {
        await _sdoLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<CanMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sdoGate) _pendingSdo = tcs;

            _bus.Send(CanOpenDefs.SdoRequest(_node), Sdo.BuildUploadRequest(a));

            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (done != tcs.Task)
                return null; // timeout

            var resp = await tcs.Task.ConfigureAwait(false);
            return Sdo.TryParseUploadResponse(resp.Data, a, out var data) ? data : null;
        }
        finally
        {
            lock (_sdoGate) _pendingSdo = null;
            _sdoLock.Release();
        }
    }

    /// <summary>Expedited SDO download (write) of 1..4 bytes. Returns true on a confirmed write.</summary>
    public async Task<bool> SdoDownloadAsync(ObjectAddress a, byte[] value, int timeoutMs = 200, CancellationToken ct = default)
    {
        await _sdoLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<CanMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sdoGate) _pendingSdo = tcs;

            _bus.Send(CanOpenDefs.SdoRequest(_node), Sdo.BuildDownloadRequest(a, value));

            var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (done != tcs.Task)
                return false;

            var resp = await tcs.Task.ConfigureAwait(false);
            return Sdo.IsDownloadAck(resp.Data, a);
        }
        finally
        {
            lock (_sdoGate) _pendingSdo = null;
            _sdoLock.Release();
        }
    }

    public byte[]? SdoUpload(ObjectAddress a, int timeoutMs = 200) =>
        SdoUploadAsync(a, timeoutMs).GetAwaiter().GetResult();

    // ---- PDO ----------------------------------------------------------------
    /// <summary>
    /// Subscribes to an object's value. If the object is carried by a TxPDO (per the device's
    /// mapping), decodes it from each on-change frame; otherwise falls back to periodic SDO upload
    /// at <paramref name="fallbackPollMs"/>. Returns an unsubscribe handle.
    /// </summary>
    public IDisposable Observe<T>(ObjectAddress a, Action<T> onValue, int fallbackPollMs = 100) where T : unmanaged
    {
        EnsurePdoMap();
        if (_pdoMap is not null && _pdoMap.TryGet(a, out var entry))
        {
            void Handler(CanMessage m)
            {
                if (TryDecode<T>(m.Data, entry.ByteOffset, out var v))
                    onValue(v);
            }
            return Subscribe(entry.CobId, Handler);
        }

        _logger.LogInformation("Observe {Addr}: not PDO-mapped, falling back to SDO poll @ {Ms}ms", a, fallbackPollMs);
        var timer = new Timer(_ =>
        {
            var d = SdoUpload(a, Math.Min(fallbackPollMs, 100));
            if (d is not null && TryDecode<T>(d, 0, out var v))
                onValue(v);
        }, null, 0, fallbackPollMs);
        lock (_subGate) _timers.Add(timer);
        return new ActionDisposable(() => { timer.Dispose(); lock (_subGate) _timers.Remove(timer); });
    }

    /// <summary>Subscribes to raw frames on a COB-ID (e.g. a whole TxPDO). Returns an unsubscribe handle.</summary>
    public IDisposable Subscribe(uint cobId, Action<CanMessage> handler)
    {
        lock (_subGate)
        {
            if (!_subscribers.TryGetValue(cobId, out var list))
                _subscribers[cobId] = list = new List<Action<CanMessage>>();
            list.Add(handler);
        }
        return new ActionDisposable(() =>
        {
            lock (_subGate)
                if (_subscribers.TryGetValue(cobId, out var list))
                    list.Remove(handler);
        });
    }

    /// <summary>Creates a cyclic RxPDO image, re-transmitted every <paramref name="period"/> until disposed.</summary>
    public RxImage Rx(uint cobId, int dlc, TimeSpan period)
    {
        var image = new RxImage(cobId, dlc, period);
        var timer = new Timer(_ =>
        {
            try { _bus.Send(image.CobId, image.Snapshot()); }
            catch (Exception ex) { _logger.LogDebug(ex, "RxPDO tx failed (will retry next tick)"); }
        }, null, TimeSpan.Zero, period);
        lock (_subGate) _timers.Add(timer);
        return image;
    }

    /// <summary>Reads the TxPDO mapping (0x1A00..0x1A03) via SDO to route <see cref="Observe{T}"/>.</summary>
    public void EnsurePdoMap()
    {
        if (_pdoMap is not null)
            return;
        var map = new PdoMap();
        for (int pdo = 0; pdo < 4; pdo++)
        {
            ushort mapIdx = (ushort)(0x1A00 + pdo);
            byte? count = SdoUpload(new ObjectAddress(mapIdx, 0)) is { Length: > 0 } c ? c[0] : null;
            if (count is null or 0)
                continue;
            uint cob = CanOpenDefs.TxPdoCobForMappingIndex(mapIdx, _node);
            int offsetBits = 0;
            for (byte s = 1; s <= count.Value; s++)
            {
                var raw = SdoUpload(new ObjectAddress(mapIdx, s));
                if (raw is null || raw.Length < 4)
                    break;
                uint packed = (uint)(raw[0] | (raw[1] << 8) | (raw[2] << 16) | (raw[3] << 24));
                var (addr, bits) = PdoMap.DecodeEntry(packed);
                map.Add(new PdoMapEntry(addr, cob, offsetBits / 8, bits));
                offsetBits += bits;
            }
        }
        _pdoMap = map;
    }

    // ---- receive loop -------------------------------------------------------
    private void ReceiveLoop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            if (!_bus.TryReceive(out var msg))
                continue;
            Dispatch(msg);
        }
    }

    private void Dispatch(CanMessage msg)
    {
        if (msg.Id == _sdoResp)
        {
            TaskCompletionSource<CanMessage>? pending;
            lock (_sdoGate) pending = _pendingSdo;
            pending?.TrySetResult(msg);
            return;
        }
        if (msg.Id == _bootup && msg.Data.Length == 1 && msg.Data[0] == 0x00)
        {
            BootupReceived?.Invoke();
            return;
        }
        if (msg.Id == _emcy && msg.Data.Length >= 2)
        {
            EmcyReceived?.Invoke((ushort)(msg.Data[0] | (msg.Data[1] << 8)));
            return;
        }

        Action<CanMessage>[]? handlers = null;
        lock (_subGate)
            if (_subscribers.TryGetValue(msg.Id, out var list) && list.Count > 0)
                handlers = list.ToArray();
        if (handlers is null)
            return;
        foreach (var h in handlers)
        {
            try { h(msg); }
            catch (Exception ex) { _logger.LogWarning(ex, "TxPDO subscriber threw"); }
        }
    }

    private static bool TryDecode<T>(byte[] data, int offset, out T value) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        if (offset < 0 || offset + size > data.Length)
        {
            value = default;
            return false;
        }
        value = MemoryMarshal.Read<T>(data.AsSpan(offset, size));
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _rxThread?.Join(200); } catch { /* ignore */ }
        lock (_subGate)
        {
            foreach (var t in _timers) t.Dispose();
            _timers.Clear();
            _subscribers.Clear();
        }
        _cts.Dispose();
        _sdoLock.Dispose();
    }

    private sealed class ActionDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() { Interlocked.Exchange(ref _dispose, null)?.Invoke(); }
    }
}
