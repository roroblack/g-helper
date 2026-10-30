namespace GHelper.Input;

// Pure event reducer, shared with the headless regression tests. No Fn-state guessing.
internal sealed class CopilotKeyState
{
    private readonly HashSet<int> pressed = new();
    private bool copilotDown, appsDown, pendingCopilot;
    internal readonly record struct Result(bool Consume = false, string? Action = null, bool NeutralizeWin = false);
    internal Result Process(int vk, bool down, bool injected)
    {
        // keybd_event/SendInput from our actions (including Menu) must never recurse.
        if (injected) return new();
        if (down) pressed.Add(vk); else pressed.Remove(vk);
        bool win = pressed.Contains(0x5B) || pressed.Contains(0x5C);
        bool shift = pressed.Contains(0xA0) || pressed.Contains(0xA1) || pressed.Contains(0x10);
        bool other = pressed.Contains(0x11) || pressed.Contains(0xA2) || pressed.Contains(0xA3)
                  || pressed.Contains(0x12) || pressed.Contains(0xA4) || pressed.Contains(0xA5);
        if (vk == 0x86) // F23; consume its entire down/repeat/up sequence.
        {
            if (down && !copilotDown && win && shift && !other)
            {
                copilotDown = true;
                return new(true, null, true);
            }
            if (copilotDown)
            {
                if (!down) { copilotDown = false; pendingCopilot = true; }
                return Finish(true, win, shift);
            }
        }
        if (vk == 0x5D) // Observed standalone Apps is Fn+Copilot.
        {
            if (down && !appsDown && !win && !shift && !other) appsDown = true;
            if (appsDown)
            {
                if (!down) { appsDown = false; return new(true, "fncopilot"); }
                return new(true);
            }
        }
        return Finish(false, win, shift);
    }
    private Result Finish(bool consume, bool win, bool shift)
    {
        if (pendingCopilot && !win && !shift)
        {
            pendingCopilot = false;
            return new(consume, "copilot");
        }
        return new(consume);
    }
}
