using GHelper.Mode;
namespace GHelper;
public partial class SettingsForm
{
    public void ShowModeRequest(long id, int mode, string status, bool refresh)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(() => ShowModeRequest(id, mode, status, refresh)); return; }
        if (!Program.modeControl.IsCurrentRequest(id)) return; // Check after UI dispatch, not before enqueue.
        if (refresh) { ShowMode(Modes.GetCurrent()); FansInit(); }
        SetModeLabel($"{Modes.GetName(mode)}: {status}");
    }
}
