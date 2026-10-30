using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GHelper.Fan;

internal sealed record N1xReading(double? Value, string Unit, string Status)
{
    public string Display => Value is double v ? (Unit == "V" ? $"{v:0.###} {Unit}" : $"{v:0.##} {Unit}") : "N/A";
    internal static N1xReading Missing(string unit, string why) => new(null, unit, why);
}

internal sealed record N1xTelemetrySnapshot(DateTimeOffset Timestamp, N1xReading CpuFan,
    N1xReading GpuFan, N1xReading CpuTemperature, N1xReading CpuPower,
    N1xReading GpuTemperature, N1xReading GpuPower, string GpuName)
{
    public N1xReading GpuVoltage { get; init; } = N1xReading.Missing("V", "NVAPI GPU voltage not sampled");
    public N1xReading CpuMemoryPower { get; init; } = N1xReading.Missing("W", "CPU+Memory shared power not sampled; not CPU-only watts");
    internal static N1xTelemetrySnapshot Missing(string reason) => new(DateTimeOffset.UtcNow,
        N1xReading.Missing("RPM", reason), N1xReading.Missing("RPM", reason),
        N1xReading.Missing("°C", reason), N1xReading.Missing("W", "CPU power unsupported: source unknown; " + reason),
        N1xReading.Missing("°C", reason), N1xReading.Missing("W", reason), "N/A")
        { CpuMemoryPower = N1xReading.Missing("W", "CPU+Memory shared, not CPU-only; " + reason) };
}

// No vendor control DLL execution, INIT/DEVS, CPU-vendor heuristics, thermal-zone relabeling,
// limits-as-draw substitution, or kernel driver installation.
internal sealed class N1xTelemetryReader
{
    internal static readonly N1xTelemetryReader Shared = new();
    private readonly object gate = new();
    private readonly object publicationGate = new();
    private readonly object requestGate = new();
    private readonly Func<N1xTelemetrySnapshot> read;
    private readonly Func<long> clock;
#if TELEMETRY_TEST_HOOKS
    internal Action? BeforePublication;
    internal Action? AfterDeadlineCheck;
    internal Action? BeforeTimeoutCancellation;
#endif
    private sealed record Publication(long SampleStarted, N1xTelemetrySnapshot Snapshot);
    private Publication? published;
    private Task<N1xTelemetrySnapshot>? pending;
    private Task<Publication?>? worker;
    private bool Valid(Publication? p, long now) => p is not null && now - p.SampleStarted < 2000;
    private Publication? CurrentPublication()
    {
        var p = Volatile.Read(ref published);
        return Valid(p, clock()) ? p : null;
    }
    private N1xTelemetrySnapshot? Current() => CurrentPublication()?.Snapshot;
    internal N1xTelemetrySnapshot PeekAndRequest()
    {
        var current = Current();
        if (current is not null) return current;
        _ = RequestAsync();
        return N1xTelemetrySnapshot.Missing("sensor read pending/stale");
    }
    internal Task<N1xTelemetrySnapshot> RequestAsync()
    {
        lock (requestGate)
        {
            var current = Current();
            if (current is not null) return Task.FromResult(current);
            if (pending is { IsCompleted: false }) return pending;
            if (worker is { IsCompleted: false }) return Task.FromResult(N1xTelemetrySnapshot.Missing("sensor read still in progress"));
            long started = clock();
            var work = worker = Task.Run(() => Acquire(CancellationToken.None, true));
            return pending = Task.Run(() => CompleteRequest(work, started));
        }
    }
    private async Task<N1xTelemetrySnapshot> CompleteRequest(Task<Publication?> work, long started)
    {

        try
        {
            var candidate = await work.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
#if TELEMETRY_TEST_HOOKS
            AfterDeadlineCheck?.Invoke();
#endif
            lock (publicationGate)
            {
                long now = clock();
                if (now - started > 1000 || !Valid(candidate, now))
                    return N1xTelemetrySnapshot.Missing("sensor read timed out, stale, or in progress");
                var current = CurrentPublication();
                if (current is not null) return current.Snapshot;
                Volatile.Write(ref published, candidate);
                return candidate!.Snapshot;
            }
        }
        catch (TimeoutException)
        {
#if TELEMETRY_TEST_HOOKS
            BeforeTimeoutCancellation?.Invoke();
#endif
            return N1xTelemetrySnapshot.Missing("sensor read timed out; native call may still be running");
        }
    }
    internal N1xTelemetryReader(Func<N1xTelemetrySnapshot>? read = null, Func<long>? clock = null)
    { this.read = read ?? ReadNative; this.clock = clock ?? (() => Environment.TickCount64); }
    internal N1xTelemetrySnapshot Sample(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Monitor.TryEnter(gate)) return N1xTelemetrySnapshot.Missing("sensor read in progress");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = CurrentPublication();
            if (cached is not null) return cached.Snapshot;
            var candidate = ReadCandidate(cancellationToken);
#if TELEMETRY_TEST_HOOKS
            BeforePublication?.Invoke();
#endif
            lock (publicationGate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var current = CurrentPublication();
                if (current is not null) return current.Snapshot;
                if (!Valid(candidate, clock())) return N1xTelemetrySnapshot.Missing("sensor read stale");
                Volatile.Write(ref published, candidate);
                return candidate.Snapshot;
            }
        }
        finally { Monitor.Exit(gate); }
    }
    private Publication? Acquire(CancellationToken token, bool waitForInFlight)
    {
        token.ThrowIfCancellationRequested();
        if (waitForInFlight) { if (!Monitor.TryEnter(gate, 500)) return null; }
        else if (!Monitor.TryEnter(gate)) return null;
        try
        {
            token.ThrowIfCancellationRequested();
            return CurrentPublication() ?? ReadCandidate(token);
        }
        finally { Monitor.Exit(gate); }
    }
    private Publication ReadCandidate(CancellationToken token)
    {
        long started = clock();
        N1xTelemetrySnapshot snapshot;
        try { snapshot = read(); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            token.ThrowIfCancellationRequested();
            snapshot = N1xTelemetrySnapshot.Missing(ex.GetType().Name + ": " + ex.Message);
        }
        return new Publication(started, snapshot);
    }

    internal static N1xReading DecodeDsts(bool ok, uint returned, byte[] bytes, string unit, double scale, double maximum, int error = 0)
    {
        if (!ok) return N1xReading.Missing(unit, $"DSTS failed: Win32 {error}");
        if (returned < 4 || bytes.Length < 4) return N1xReading.Missing(unit, $"short DSTS response ({returned})");
        uint raw = BitConverter.ToUInt32(bytes, 0);
        if (raw == uint.MaxValue || raw == 0xFFFFFFFE || (raw & 0x10000) == 0)
            return N1xReading.Missing(unit, $"DSTS unsupported/raw=0x{raw:X8}");
        double value = (raw & 0xFFFF) * scale;
        if (value > maximum) return N1xReading.Missing(unit, $"DSTS out of range/raw=0x{raw:X8}");
        return new(value, unit, $"DSTS raw=0x{raw:X8}");
    }

    internal static N1xReading DecodeNvml(int rc, uint raw, bool power)
    {
        string unit = power ? "W" : "°C";
        if (rc != 0) return N1xReading.Missing(unit, $"NVML return code {rc}");
        double value = power ? raw / 1000d : raw;
        if (raw == uint.MaxValue || value > (power ? 1000 : 125))
            return N1xReading.Missing(unit, $"NVML out of range/raw={raw}");
        return new(value, unit, $"NVML raw={raw}" + (power ? " mW" : " °C"));
    }

    internal static bool IsTargetGpu(string name) => name.Contains("N1X", StringComparison.OrdinalIgnoreCase)
        && name.Contains("RTX", StringComparison.OrdinalIgnoreCase) && !name.Contains("NPU", StringComparison.OrdinalIgnoreCase);

    private static N1xTelemetrySnapshot ReadNative()
    {
        var missing = N1xTelemetrySnapshot.Missing("not queried");
        N1xReading Read(SafeFileHandle handle, uint id, string unit, double scale, double max)
        {
            if (handle.IsInvalid) return N1xReading.Missing(unit, "ATKACPI open failed");
            byte[] input = new byte[16], output = new byte[16];
            BitConverter.GetBytes(0x53545344u).CopyTo(input, 0); // DSTS only
            BitConverter.GetBytes(8u).CopyTo(input, 4);
            BitConverter.GetBytes(id).CopyTo(input, 8);
            bool ok = DeviceIoControl(handle, 0x22240c, input, 16, output, 16, out uint count, IntPtr.Zero);
            int error = ok ? 0 : Marshal.GetLastWin32Error();
            var result = DecodeDsts(ok, count, output, unit, scale, max, error);
            return result with { Status = $"0x{id:X8}: " + result.Status };
        }
        using var h = CreateFileW(@"\\.\ATKACPI", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        int openError = h.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        missing = missing with {
            CpuFan = Read(h, 0x110013, "RPM", 100, 12000),
            GpuFan = Read(h, 0x110014, "RPM", 100, 12000),
            CpuTemperature = Read(h, 0x120094, "°C", 1, 125),
            // DSTS 0x00120079 is CPU voltage, not power; its voltage scale is unknown.
            // Never probe it for watts or substitute GPU/module/profile power.
            CpuPower = N1xReading.Missing("W", "CPU power unsupported: source unknown (voltage endpoint is not watts)")
        };
        if (h.IsInvalid) missing = N1xTelemetrySnapshot.Missing($"ATKACPI open failed: Win32 {openError}");
        var nvapi = N1xNvapi.Read(out var sharedPower);
        missing = missing with { GpuTemperature = nvapi.Temperature, GpuVoltage = nvapi.Voltage, CpuMemoryPower = sharedPower, GpuName = "NVAPI physical GPU (single device)" };
        try
        {
            int rc = nvmlInit_v2();
            if (rc != 0) return GpuError(missing, $"NVML init return code {rc}");
            try
            {
                rc = nvmlDeviceGetCount_v2(out uint count);
                if (rc != 0) return GpuError(missing, $"NVML count return code {rc}");
                for (uint i = 0; i < Math.Min(count, 32); i++)
                {
                    if (nvmlDeviceGetHandleByIndex_v2(i, out IntPtr device) != 0) continue;
                    var name = new StringBuilder(256);
                    if (nvmlDeviceGetName(device, name, 256) != 0 || !IsTargetGpu(name.ToString())) continue;
                    int powerRc = nvmlDeviceGetPowerUsage(device, out uint power);
                    return missing with { GpuName = name.ToString(), GpuPower = DecodeNvml(powerRc, power, true) };
                }
                return GpuError(missing, "NVML: RTX N1X device not found (NPU is not GPU)");
            }
            finally { nvmlShutdown(); }
        }
        catch (Exception ex) { return GpuError(missing, ex.GetType().Name + ": " + ex.Message); }
    }

    private static N1xTelemetrySnapshot GpuError(N1xTelemetrySnapshot s, string error) => s with {
        GpuPower = N1xReading.Missing("W", error)
    };

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] input, uint inputLength, byte[] output, uint outputLength, out uint returned, IntPtr overlapped);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll")] private static extern int nvmlShutdown();
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetCount_v2(out uint count);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll", CharSet = CharSet.Ansi)] private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, uint sensor, out uint value);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
}
