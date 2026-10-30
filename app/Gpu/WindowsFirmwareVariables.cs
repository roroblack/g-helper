using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace GHelper;

// Invoked only on an explicit user request. No initialization-time firmware or
// privilege calls. The test suite substitutes IFirmwareVariables before use.
internal sealed class WindowsFirmwareVariables : IFirmwareVariables
{
    private static readonly object PrivilegeLock = new();
    internal string LastFailureStage { get; private set; } = "";
    private readonly ITokenPrivileges native;
    internal WindowsFirmwareVariables(ITokenPrivileges? native = null) => this.native = native ?? new NativeTokenPrivileges();

    internal interface ITokenPrivileges
    {
        bool Open(out SafeAccessTokenHandle token, out int error);
        bool Lookup(out Luid luid, out int error);
        bool Adjust(SafeAccessTokenHandle token, ref TokenPrivileges state, out TokenPrivileges previous, out int error);
    }

    private sealed class NativeTokenPrivileges : ITokenPrivileges
    {
        public bool Open(out SafeAccessTokenHandle token, out int error)
        {
            bool ok = OpenProcessToken(new IntPtr(-1), 0x20 | 0x8, out token);
            error = ok ? 0 : Marshal.GetLastPInvokeError();
            return ok;
        }
        public bool Lookup(out Luid luid, out int error)
        {
            bool ok = LookupPrivilegeValueW(null, "SeSystemEnvironmentPrivilege", out luid);
            error = ok ? 0 : Marshal.GetLastPInvokeError();
            return ok;
        }
        public bool Adjust(SafeAccessTokenHandle token, ref TokenPrivileges state, out TokenPrivileges previous, out int error)
        {
            // SetLastError=true clears/captures the native last-error on .NET 10;
            // clear the managed cache too, never reinterpret a prior API failure.
            Marshal.SetLastPInvokeError(0);
            bool ok = AdjustTokenPrivileges(token, false, ref state, (uint)Marshal.SizeOf<TokenPrivileges>(), out previous, out _);
            error = Marshal.GetLastPInvokeError();
            return ok;
        }
    }

    public bool Write(string name, string guid, byte[]? data, uint attributes, out int error)
        => WithPrivilege(() =>
        {
            bool ok = SetFirmwareEnvironmentVariableExW(name, guid, data, (uint)(data?.Length ?? 0), attributes);
            return (ok, ok ? 0 : Marshal.GetLastWin32Error());
        }, out error);

    public bool Read(string name, string guid, out byte[] data, out uint attributes, out int error)
    {
        byte[] buffer = new byte[4];
        uint attrs = 0;
        uint size = 0;
        bool success = WithPrivilege(() =>
        {
            size = GetFirmwareEnvironmentVariableExW(name, guid, buffer, (uint)buffer.Length, out attrs);
            return (size != 0, size != 0 ? 0 : Marshal.GetLastWin32Error());
        }, out error);
        data = success ? buffer.AsSpan(0, checked((int)size)).ToArray() : [];
        attributes = attrs;
        return success;
    }

    internal bool WithPrivilege(Func<(bool Success, int Error)> action, out int error)
    {
        lock (PrivilegeLock)
        {
            LastFailureStage = "OpenProcessToken";
            if (!native.Open(out SafeAccessTokenHandle token, out error)) return false;
            using (token)
            {
                LastFailureStage = "LookupPrivilegeValue(SeSystemEnvironmentPrivilege)";
                if (!native.Lookup(out Luid luid, out error)) return false;
                TokenPrivileges requested = new() { Count = 1, Luid = luid, Attributes = 2 };
                LastFailureStage = "AdjustTokenPrivileges(enable SeSystemEnvironmentPrivilege)";
                bool adjusted = native.Adjust(token, ref requested, out TokenPrivileges previous, out int adjustError);
                Logger.WriteLine($"VRAM privilege enable: return={adjusted} Win32={adjustError} previousCount={previous.Count}");
                // TRUE + 1300 means the token does not contain the privilege.
                // Enabling cannot add it: UAC elevation (or policy assignment) is required.
                if (!adjusted || adjustError != 0)
                {
                    error = adjustError == 0 ? 1314 : adjustError;
                    return false;
                }
                (bool Success, int Error) result;
                bool restored = true;
                int restoreError = 0;
                try
                {
                    LastFailureStage = "FirmwareEnvironmentVariable API";
                    result = action();
                }
                finally
                {
                    // Count0 means it was already enabled; preserve that state.
                    if (previous.Count != 0)
                        restored = native.Adjust(token, ref previous, out _, out restoreError);
                    Logger.WriteLine($"VRAM privilege restore: return={restored} Win32={restoreError}");
                }
                error = result.Error;
                if (!restored || restoreError != 0)
                {
                    LastFailureStage = "AdjustTokenPrivileges(restore)";
                    error = restoreError == 0 ? 1314 : restoreError;
                    return false;
                }
                if (result.Success) LastFailureStage = "";
                return result.Success;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct TokenPrivileges { public uint Count; public Luid Luid; public uint Attributes; }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out Luid luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TokenPrivileges state, uint length, out TokenPrivileges previous, out uint returnedLength);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFirmwareEnvironmentVariableExW(string name, string guid, [In] byte[]? data, uint size, uint attributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFirmwareEnvironmentVariableExW(string name, string guid, [Out] byte[] data, uint size, out uint attributes);
}
