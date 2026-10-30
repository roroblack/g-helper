using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace GHelper.Mode;

internal sealed record PowerPlan(Guid Id, string Name)
{
    public override string ToString() => $"{Name} [{Id:D}]";
}

// Default-denied adapter. Only an opted-in current association inside the shared mode gate authorizes a write.
internal sealed class PowerPlanNative
{
    internal interface ICalls
    {
        uint Enumerate(uint index, out Guid id);
        string FriendlyName(Guid id);
        uint Get(out IntPtr pointer);
        uint Set(Guid id);
        void Free(IntPtr pointer);
    }
    private readonly ICalls calls;
    private readonly Func<bool> writeAuthorized;
    public PowerPlanNative() : this(new Win32Calls(), () => false) { }
    // Caller holds the shared mode gate; authorization is re-read at the actual native boundary.
    public PowerPlanNative(Func<bool> writeAuthorized) : this(new Win32Calls(), writeAuthorized) { }
    internal PowerPlanNative(ICalls calls, bool allowInjectedWrites = false) : this(calls, () => allowInjectedWrites) { }
    internal PowerPlanNative(ICalls calls, Func<bool> writeAuthorized) { this.calls = calls; this.writeAuthorized = writeAuthorized; }
    public IReadOnlyList<PowerPlan> Enumerate()
    {
        var plans = new List<PowerPlan>();
        for (uint i = 0; i < 4096; i++)
        {
            uint status = calls.Enumerate(i, out var id);
            if (status == 259) return plans;
            if (status != 0) throw new Win32Exception((int)status, "PowerEnumerate");
            if (id == Guid.Empty || plans.Any(p => p.Id == id)) throw new InvalidOperationException("Invalid/duplicate plan GUID");
            // A missing friendly name cannot change the identity or hide an otherwise valid plan.
            string name;
            try { name = calls.FriendlyName(id); } catch { name = "이름 읽기 실패"; }
            plans.Add(new(id, name));
        }
        throw new InvalidOperationException("PowerEnumerate limit exceeded");
    }
    public (uint Status, Guid Active) GetActive()
    {
        IntPtr pointer = IntPtr.Zero;
        try
        {
            uint status = calls.Get(out pointer);
            if (status != 0) return (status, Guid.Empty);
            return pointer == IntPtr.Zero ? (13u, Guid.Empty) : (0u, Marshal.PtrToStructure<Guid>(pointer));
        }
        finally { if (pointer != IntPtr.Zero) calls.Free(pointer); }
    }
    public uint SetActive(Guid id) => id != Guid.Empty && writeAuthorized() ? calls.Set(id) : 5;

    private sealed class Win32Calls : ICalls
    {
        public uint Enumerate(uint index, out Guid id)
        {
            uint bytes = 16;
            return PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, index, out id, ref bytes);
        }
        public string FriendlyName(Guid id)
        {
            uint bytes = 0;
            uint status = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref bytes);
            if (status != 0 && status != 234) throw new Win32Exception((int)status);
            if (bytes == 0 || bytes > 65536 || bytes % 2 != 0) throw new InvalidOperationException("Invalid friendly name length");
            byte[] buffer = new byte[bytes];
            status = PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buffer, ref bytes);
            if (status != 0 || bytes > buffer.Length) throw new Win32Exception((int)status);
            return Encoding.Unicode.GetString(buffer, 0, (int)bytes).TrimEnd('\0');
        }
        public uint Get(out IntPtr pointer) => PowerGetActiveScheme(IntPtr.Zero, out pointer);
        public uint Set(Guid id) => PowerSetActiveScheme(IntPtr.Zero, ref id);
        public void Free(IntPtr pointer) => LocalFree(pointer);
        [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access, uint index, out Guid buffer, ref uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, [Out] byte[]? buffer, ref uint size);
        [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
        // const GUID* is passed by reference on ARM64, never as a by-value struct.
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
        [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    }
}
