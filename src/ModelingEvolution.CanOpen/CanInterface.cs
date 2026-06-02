using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SocketCANSharp;
using SocketCANSharp.Network;

namespace ModelingEvolution.CanOpen;

/// <summary>
/// A Linux SocketCAN raw interface: bring-up (ip link bitrate + optional GPIO terminator),
/// thread-safe send, and timed receive. Promoted from the bench-validated FroniusCanbus wrapper.
/// Linux-only.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class CanInterface : IDisposable
{
    private readonly RawCanSocket _socket;
    private readonly Lock _writeGate = new();

    private CanInterface(RawCanSocket socket) => _socket = socket;

    [DllImport("libc")]
    private static extern uint geteuid();

    public static bool IsRoot => geteuid() == 0;

    /// <summary>
    /// Configures the interface (active mode, BERR reporting, auto-restart) at <paramref name="bitrate"/>
    /// and, if given, raises a GPIO terminator line (e.g. "gpiochip2:8"). Requires root.
    /// In a containerized deploy prefer bringing the interface up on the host and skipping this.
    /// </summary>
    public static void BringUp(string iface, int bitrate, string? terminatorGpio = null)
    {
        Run("ip", $"link set {iface} down", check: false);
        Run("ip", $"link set {iface} type can bitrate {bitrate} listen-only off berr-reporting on restart-ms 1000");
        Run("ip", $"link set {iface} up");

        if (!string.IsNullOrWhiteSpace(terminatorGpio))
        {
            var parts = terminatorGpio.Split(':', 2);
            if (parts.Length == 2)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("gpioset")
                    {
                        ArgumentList = { "--mode=signal", parts[0], $"{parts[1]}=1" },
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    });
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[canopen] terminator gpioset failed: {ex.Message}");
                }
            }
        }
        Thread.Sleep(200);
    }

    /// <summary>Opens a raw CAN socket bound to <paramref name="iface"/> with a blocking-read timeout.</summary>
    public static CanInterface Open(string iface, int receiveTimeoutMs = 20)
    {
        CanNetworkInterface dev = CanNetworkInterface.GetAllInterfaces(true)
            .FirstOrDefault(i => i.Name.Equals(iface, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"CAN interface '{iface}' not found. Is it up (BringUp / host) and are you root?");

        var socket = new RawCanSocket { ReceiveTimeout = receiveTimeoutMs };
        socket.Bind(dev);
        return new CanInterface(socket);
    }

    /// <summary>Sends a standard (11-bit) frame. DLC is derived from <paramref name="data"/>.Length. Thread-safe.</summary>
    public void Send(uint cobId, ReadOnlySpan<byte> data)
    {
        var frame = new CanFrame(cobId, data.ToArray());
        lock (_writeGate)
            _socket.Write(frame);
    }

    /// <summary>Receives one frame; false on timeout.</summary>
    public bool TryReceive(out CanMessage message)
    {
        message = default;
        try
        {
            int n = _socket.Read(out CanFrame frame);
            if (n <= 0)
                return false;
            byte len = Math.Min(frame.Length, (byte)8);
            var data = new byte[len];
            Array.Copy(frame.Data, data, len);
            message = new CanMessage(frame.CanId & 0x7FF, data);
            return true;
        }
        catch (SocketCanException)
        {
            return false; // SO_RCVTIMEO → EAGAIN surfaces as an exception in SocketCANSharp
        }
    }

    public void Dispose() => _socket.Dispose();

    private static void Run(string file, string args, bool check = true)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"failed to start {file}");
        p.WaitForExit();
        if (check && p.ExitCode != 0)
            throw new InvalidOperationException($"`{file} {args}` exited {p.ExitCode}: {p.StandardError.ReadToEnd()}");
    }
}
