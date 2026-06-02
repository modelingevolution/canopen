# ModelingEvolution.CanOpen

Layer-1 **CANopen** protocol client over Linux **SocketCAN** — the CANopen analog of
`ModelingEvolution.Modbus`. Pure protocol: **no device or vendor vocabulary**. It is the foundation
for CANopen device adapters (e.g. the Fronius TPS 4000 welder — Epic 036).

> **Status:** seeded from the bench-proven `FroniusCanbus` integration (validated against a real
> Beckhoff BK5120 + Fronius TPS 4000 on a Jetson reServer, 2026-06-02). Version `0.1.0` — preview.

## Layer model

```
Layer 0  SocketCANSharp            raw CAN 2.0 frames (this package depends on it)
Layer 1  ModelingEvolution.CanOpen ← this package: NMT, SDO, PDO, object-dictionary addressing
Layer 2  ModelingEvolution.CanOpen.TaggedDevice   (future) named-tag device
Layer 3  vendor adapters           (e.g. FroniusWeldingMachine_TPS4000)
```
See `docs/epics/cross-cutting/canopen-layers.md` in the project-management repo.

## What's in the box

- **`CanInterface`** — SocketCAN bring-up (`ip link` bitrate + optional GPIO terminator), thread-safe
  send, timed receive. Linux-only, requires root for bring-up.
- **`CanOpenClient`** — one background receive loop demultiplexing by COB-ID:
  - **NMT** — `NmtStart()`, `NmtPreOperational()`, `Nmt(cmd)`
  - **SDO** — expedited `SdoUploadAsync` / `SdoDownloadAsync` (+ sync `SdoUpload`)
  - **TxPDO** — `Observe<T>(ObjectAddress, onValue)` routes via the device's PDO mapping
    (read once from `0x1A00..0x1A03`), or falls back to periodic SDO poll
  - **RxPDO** — `Rx(cobId, dlc, period)` returns a cyclic `RxImage` re-transmitted every period
    (the watchdog feed); mutate bits/bytes, the client sends snapshots
  - events `BootupReceived`, `EmcyReceived`
- **`ObjectAddress`** — `(Index, Sub)` object-dictionary address.
- **`Sdo`** — pure (de)serialization of expedited SDO frames (unit-testable, no I/O).
- **`PdoMap`** — decoded TxPDO mapping.

## Quick example

```csharp
using ModelingEvolution.CanOpen;

CanInterface.BringUp("can0", 500_000, terminatorGpio: "gpiochip2:8"); // host/root; skip if up already
using var bus = CanInterface.Open("can0");
using var co = new CanOpenClient(bus, nodeId: 5);
co.Start();

co.NmtStart();                                            // -> Operational
co.Observe<ushort>(new ObjectAddress(0x6401, 2),          // welding current actual (TxPDO2)
    raw => Console.WriteLine($"I = {raw / 65.535f:F0} A"));

var rx1 = co.Rx(CanOpenDefs.RxPdo(5, 1), dlc: 4, TimeSpan.FromMilliseconds(100)); // cyclic control
rx1.SetBit(0, 1, true);                                   // Robot-ready (held; watchdog fed @100ms)
```

## Platform

Linux only (SocketCAN). Validated on ARM64 (Jetson) and x64. Bring-up needs root (`ip link`,
`gpioset`); in containers prefer bringing `can0` up on the host and only opening the socket here.

## Releasing

Per org convention: **never publish locally.** Push a branch → CI publishes a preview
(`X.Y.Z-<branch>-<sha>`); `release.sh` / a `v*` tag → CI publishes the stable release.
