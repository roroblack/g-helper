using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace GHelper.Fan;

// Read-only allowlist. Public thermal contract: NVIDIA 70d337db9186e968eab622f7e786de7e437faf3d.
// Private voltage contract: NvAPIWrapper 2a41eb4d6b1bceff5dbb33a3b5ea70bd582214a7,
// PrivateVoltageStatusV1 (Pascal Only annotation); N1X support observed separately.
// Local sentinel/version/range rejection is conservative policy, not vendor validity flags.
internal static class N1xNvapi
{
    private const string DllPath = @"C:\Windows\System32\nvapia64.dll";
    private const string ExpectedHash = "01EA347D7917CB07781D3112E0840FCBCE8474304BC30CF0F5C2B1A872A3171F";
    private static readonly object Gate = new();
    private static nint module;
    private static Query? query;
    private static bool initialized;
    private static nint physicalGpu;
    private static ReadOnlySharedPowerAdapter? sharedAdapter;
    internal static SharedPowerGetter.Receipt? LastSharedReceipt { get; private set; }
    internal static readonly List<string> SharedPointerEvidence = new();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Enumerate([Out] nint[] h, out uint count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Thermal(nint h, uint selector, nint buffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Voltage(nint h, nint buffer);

    internal static N1xReading DecodeThermal(int status, byte[] b)
    {
        if (status != 0 || b.Length != 68 || BitConverter.ToUInt32(b, 0) != 0x20044)
            return N1xReading.Missing("°C", $"NVAPI thermal status {status}; invalid version/size");
        uint count = BitConverter.ToUInt32(b, 4);
        if (count is < 1 or > 3) return N1xReading.Missing("°C", "NVAPI thermal invalid count");
        for (int i = 0; i < count; i++)
        {
            int offset = 8 + 20 * i;
            int controller = BitConverter.ToInt32(b, offset), temp = BitConverter.ToInt32(b, offset + 12);
            int target = BitConverter.ToInt32(b, offset + 16);
            if (controller > 0 && target == 1 && temp != -1 && temp != int.MinValue && temp is >= -40 and <= 150)
                return new(temp, "°C", $"NVAPI GPU target1; status0; raw={temp} °C");
        }
        return N1xReading.Missing("°C", "NVAPI: no valid GPU target1 temperature");
    }

    internal static N1xReading DecodeVoltage(int status, byte[] b)
    {
        if (status != 0 || b.Length != 76 || BitConverter.ToUInt32(b, 0) != 0x1004c)
            return N1xReading.Missing("V", $"NVAPI GPU voltage status {status}; invalid version/size");
        uint raw = BitConverter.ToUInt32(b, 40);
        if (raw is 0 or uint.MaxValue) return N1xReading.Missing("V", "NVAPI GPU voltage invalid payload (local policy)");
        return new(raw / 1000000d, "V", $"NVAPI physical GPU; status0; raw={raw} µV; private v1");
    }

    private static T Resolve<T>(uint id) where T : Delegate
    {
        nint p = query!(id);
        if (p == 0) throw new NotSupportedException($"NVAPI getter {id:X8} unavailable");
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }
    private static void EnsureLoaded()
    {
        if (query is not null) return;
        if (RuntimeInformation.ProcessArchitecture != Architecture.Arm64) throw new NotSupportedException("NVAPI requires native ARM64");
        byte[] b = File.ReadAllBytes(DllPath);
        if (Convert.ToHexString(SHA256.HashData(b)) != ExpectedHash || BitConverter.ToUInt16(b, BitConverter.ToInt32(b, 60) + 4) != 0xaa64)
            throw new NotSupportedException("NVAPI installed DLL identity changed; revalidation required");
        if (module == 0) module = LoadLibraryExW(DllPath, 0, 0x1100);
        if (module == 0) throw new InvalidOperationException("NVAPI normal load failed");
        var path = new StringBuilder(1024);
        if (GetModuleFileNameW(module, path, 1024) == 0 || !string.Equals(path.ToString(), DllPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("NVAPI loaded module path mismatch");
        var p = GetProcAddress(module, "nvapi_QueryInterface");
        if (p == 0) throw new NotSupportedException("NVAPI QueryInterface unavailable");
        query = Marshal.GetDelegateForFunctionPointer<Query>(p);
    }

    // Retain the existing GPU-only API for callers/tests; all native work uses one owner.
    internal static (N1xReading Temperature, N1xReading Voltage) Read() => Read(out _);
    internal static (N1xReading Temperature, N1xReading Voltage) Read(out N1xReading sharedPower)
    {
        sharedPower = N1xReading.Missing("W", "CPU+Memory acquisition unavailable");
        lock (Gate)
        {
            try
            {
                EnsureLoaded();
                if (!initialized)
                {
                    int init = Resolve<Initialize>(0x0150E828)();
                    if (init != 0) throw new InvalidOperationException($"NVAPI initialize {init}");
                    initialized = true;
                }
                var h = new nint[64];
                h[0] = physicalGpu;
                int en = 0; uint count = 1;
                if (physicalGpu == 0) en = Resolve<Enumerate>(0xE5AC921F)(h, out count);
                // This validated machine has exactly one physical GPU. Do not silently associate
                // NVAPI first-GPU values with a different NVML device on multi-GPU systems.
                if (en != 0 || count != 1 || h[0] == 0) throw new NotSupportedException($"NVAPI physical GPU identity ambiguous: status{en}, count{count}");
                physicalGpu = h[0];
                N1xReading temp, volt;
                try
                {
                    byte[] b = new byte[68]; BitConverter.GetBytes(0x20044u).CopyTo(b, 0);
                    for (int i = 0; i < 3; i++) { BitConverter.GetBytes(-1).CopyTo(b, 8 + i * 20); BitConverter.GetBytes(int.MinValue).CopyTo(b, 20 + i * 20); BitConverter.GetBytes(-1).CopyTo(b, 24 + i * 20); }
                    nint p = Marshal.AllocHGlobal(b.Length);
                    try { Marshal.Copy(b, 0, p, b.Length); int rc = Resolve<Thermal>(0xE3640A56)(h[0], 15, p); Marshal.Copy(p, b, 0, b.Length); temp = DecodeThermal(rc, b); }
                    finally { Marshal.FreeHGlobal(p); }
                }
                catch (Exception e) { temp = N1xReading.Missing("°C", e.Message); }
                try
                {
                    // Exact upstream constructor bytes: version1004C and 72 zero bytes.
                    byte[] b = new byte[76]; BitConverter.GetBytes(0x1004cu).CopyTo(b, 0);
                    nint p = Marshal.AllocHGlobal(b.Length);
                    try { Marshal.Copy(b, 0, p, b.Length); int rc = Resolve<Voltage>(0x465F9BCF)(h[0], p); Marshal.Copy(p, b, 0, b.Length); volt = DecodeVoltage(rc, b); }
                    finally { Marshal.FreeHGlobal(p); }
                }
                catch (Exception e) { volt = N1xReading.Missing("V", e.Message); }
                try
                {
                    sharedAdapter ??= new ReadOnlySharedPowerAdapter(ResolveSharedPointer);
                    LastSharedReceipt = sharedAdapter.Read(h[0]);
                    sharedPower = new(LastSharedReceipt.Value.Watts, "W", "CPU+Memory shared, not CPU-only; " + LastSharedReceipt.Value.Reason);
                }
                catch (Exception e) { LastSharedReceipt = null; sharedPower = N1xReading.Missing("W", "CPU+Memory: " + e.Message); }
                return (temp, volt);
            }
            catch (Exception e) { LastSharedReceipt = null; sharedPower = N1xReading.Missing("W", "CPU+Memory: " + e.Message); return (N1xReading.Missing("°C", e.Message), N1xReading.Missing("V", e.Message)); }
        }
    }
    // Private getter validation never loads a second library or initializes NVAPI.
    // Resolve through the existing normal-loaded owner, validate actual pointer module,
    // complete installed file hash, ARM64 PE and exact interface-specific RVA once.
    private static nint ResolveSharedPointer(uint id)
    {
        if (id is not (0xc12eb19e or 0xf40238ef)) throw new NotSupportedException("not a shared getter");
        nint pointer = query!(id);
        if (pointer == 0 || !GetModuleHandleExW(6, pointer, out nint owner)) throw new NotSupportedException("shared pointer module unavailable");
        var path = new StringBuilder(32768);
        if (GetModuleFileNameW(owner, path, path.Capacity) == 0) throw new NotSupportedException("shared module path unavailable");
        string actual = path.ToString(); long rva = (long)(pointer - owner);
        if (!ProbePolicy.AllowedPointer(id, actual, rva)) throw new NotSupportedException("shared pointer module/RVA changed; revalidation required");
        byte[] bytes = File.ReadAllBytes(actual);
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        ushort machine = BitConverter.ToUInt16(bytes, BitConverter.ToInt32(bytes, 60) + 4);
        if (!ProbePolicy.ValidateIdentity(actual, hash, machine, RuntimeInformation.ProcessArchitecture)) throw new NotSupportedException("shared provider identity changed; revalidation required");
        SharedPointerEvidence.Add($"{id:X8}|{actual}|{hash}|{machine:X4}|{rva:X}");
        return pointer;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetModuleHandleExW(uint flags, nint address, out nint owner);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryExW(string path, nint file, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern nint GetProcAddress(nint module, string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern uint GetModuleFileNameW(nint module, StringBuilder path, int size);
}
