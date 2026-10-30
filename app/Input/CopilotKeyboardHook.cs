using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GHelper.Input;

// Installed on the UI message-loop thread; never run actions inside the hook callback.
internal sealed class CopilotKeyboardHook : IDisposable
{
    private readonly CopilotKeyState state = new();
    private readonly HookProc callback; // Keep delegate rooted for the native hook lifetime.
    private readonly Control target;
    private readonly Action<string> dispatch;
    private nint handle;
    internal static readonly nint InjectionTag = (nint)0x4748354B;
    private delegate nint HookProc(int code, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public uint Vk, Scan, Flags, Time;
        public nuint ExtraInfo;
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nint extra);

    internal CopilotKeyboardHook(Control target, Action<string> dispatch)
    {
        this.target = target;
        this.dispatch = dispatch;
        _ = target.Handle;
        callback = OnKey;
        handle = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    private nint OnKey(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && ((int)wParam is 0x100 or 0x101 or 0x104 or 0x105))
        {
            var data = Marshal.PtrToStructure<KeyboardData>(lParam);
            bool injected = (data.Flags & 0x12) != 0 || data.ExtraInfo == (nuint)InjectionTag;
            var result = state.Process((int)data.Vk, (int)wParam is 0x100 or 0x104, injected);
            if (result.NeutralizeWin)
            {
                // The Win/Shift prefixes have already passed through. A tagged Ctrl tap
                // prevents releasing Win from opening Start; F23 never reaches Windows.
                keybd_event(0xA2, 0, 0, InjectionTag);
                keybd_event(0xA2, 0, 2, InjectionTag);
            }
            if (result.Action is string name && !target.IsDisposed)
                target.BeginInvoke(() => dispatch(name));
            if (result.Consume) return 1;
        }
        return CallNextHookEx(handle, code, wParam, lParam);
    }
    public void Dispose()
    {
        if (handle == 0) return;
        UnhookWindowsHookEx(handle);
        handle = 0;
        GC.KeepAlive(callback);
    }
}
