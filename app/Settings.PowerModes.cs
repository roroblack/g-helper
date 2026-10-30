using GHelper.Mode;
using GHelper.UI;
namespace GHelper;
public partial class SettingsForm
{
    private PerModePowerSelector windowsPowerSelector = new();
    private void InitWindowsPowerSelector()
    {
        panelPerformance.Controls.Add(windowsPowerSelector);
        panelPerformance.Controls.SetChildIndex(windowsPowerSelector, 0);
        windowsPowerSelector.Committed += (mode, choice) => Program.modeControl.SelectWindowsPower(mode, choice);
        RefreshWindowsPowerSelector(Modes.GetCurrent());
    }
    private void RefreshWindowsPowerSelector(int mode)
        => windowsPowerSelector.ShowMode(mode, Modes.GetName(mode), PerModePower.Read(AppConfig.GetString(PerModePower.Key(mode))));
    public void ShowWindowsPowerStatus(int mode, string text)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(() => ShowWindowsPowerStatus(mode, text)); return; }
        windowsPowerSelector.ShowStatus(mode, text);
    }
    public void ShowWindowsPowerStatus(int mode, string text, Func<bool> isCurrent)
    {
        if (IsDisposed || !IsHandleCreated || !isCurrent()) return;
        if (InvokeRequired) { BeginInvoke(() => ShowWindowsPowerStatus(mode, text, isCurrent)); return; }
        // Recheck after dispatch: a newer choice/mode can arrive while this was queued.
        if (isCurrent()) windowsPowerSelector.ShowStatus(mode, text);
    }
}
