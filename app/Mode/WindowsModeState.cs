namespace GHelper.Mode;

// No native I/O here: production and mocked tests share the same transition logic.
internal sealed class WindowsModeState
{
    public bool Active { get; private set; }
    private readonly Action capture;
    private readonly Action restore;
    private readonly Action baseline;
    public WindowsModeState(Action capture, Action restore, Action baseline)
    {
        this.capture = capture;
        this.restore = restore;
        this.baseline = baseline;
    }
    public void Enter()
    {
        if (Active) return; // Do not overwrite choices made in Windows Settings while active.
        baseline(); // Known GHelper Standard value, NOT a guessed vendor Windows ACPI value.
        restore();
        Active = true;
    }
    public void Leave()
    {
        if (!Active) return;
        capture();
        Active = false;
    }
}
