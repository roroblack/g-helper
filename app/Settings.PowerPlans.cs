using GHelper.Mode;
using GHelper.UI;
namespace GHelper;
public partial class SettingsForm
{
    private readonly PerModePlanSelector windowsPlanSelector = new();
    private IReadOnlyList<PowerPlan> windowsPlans = Array.Empty<PowerPlan>();
    private long planRefreshGeneration;
    private void InitWindowsPlanSelector()
    {
        // Optional separate dialog: no added main-window row or startup enumeration.
        Disposed += (_, _) => windowsPlanSelector.Dispose();
        windowsPlanSelector.EnableRequested += async selection => {
            if (!windowsPlanSelector.IsCurrent(selection) || selection.Mode != Modes.GetCurrent()) return;
            windowsPlanSelector.EnableAssociation.Enabled = false;
            windowsPlanSelector.ApplyButton.Enabled = false;
            windowsPlanSelector.Choice.Enabled = false;
            try {
                await Program.modeControl.SetWindowsPlanEnabled(selection.Mode, selection.Enabled);
                if (IsDisposed) return;
                bool current = windowsPlanSelector.IsCurrent(selection) && selection.Mode == Modes.GetCurrent();
                RefreshWindowsPlanSelector(Modes.GetCurrent());
                if (current) windowsPlanSelector.ShowStatus(windowsPlanSelector.Current, "사용 설정만 저장됨 · 지금 적용/자동 되돌리기 없음");
            }
            finally { if (!windowsPlanSelector.IsDisposed) { windowsPlanSelector.EnableAssociation.Enabled = true; windowsPlanSelector.Choice.Enabled = true; RefreshWindowsPlanSelector(Modes.GetCurrent()); } }
        };
        windowsPlanSelector.Saved += selection => {
            if (!windowsPlanSelector.IsCurrent(selection) || selection.Mode != Modes.GetCurrent()) return;
            Program.modeControl.SaveWindowsPlan(selection.Mode, selection.Plan);
        };
        windowsPlanSelector.ApplyRequested += selection => {
            if (!windowsPlanSelector.IsCurrent(selection) || selection.Mode != Modes.GetCurrent()) return;
            Program.modeControl.RequestWindowsPlanApply(selection.Mode, selection.Plan,
                text => ShowWindowsPlanStatus(selection, text));
        };
        windowsPlanSelector.RefreshRequested += RefreshWindowsPlanList;
        RefreshWindowsPlanSelector(Modes.GetCurrent());

    }
    private void RefreshWindowsPlanSelector(int mode) => windowsPlanSelector.ShowMode(mode, Modes.GetName(mode),
        PerModePlan.Read(AppConfig.GetString(PerModePlan.Key(mode))), windowsPlans, AppConfig.Get(PerModePlan.EnabledKey(mode), 0) == 1);
    internal void ShowWindowsPlanSettings(IWin32Window owner)
    {
        using var dialog = new Form { Text = "Windows 전원 관리 계획 (선택 사항)", StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(600, 420), AutoScroll = true, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, Font = Font };
        dialog.Controls.Add(windowsPlanSelector);
        // Top-docked AutoSize children are excluded from WinForms' implicit scroll extent.
        dialog.Layout += (_, _) => dialog.AutoScrollMinSize = new Size(0, windowsPlanSelector.Height);
        dialog.PerformLayout();
        RefreshWindowsPlanSelector(Modes.GetCurrent());
        RefreshWindowsPlanList(); // Get/Enumerate only; never apply on opening.
        try { dialog.ShowDialog(owner); }
        finally { ++planRefreshGeneration; dialog.Controls.Remove(windowsPlanSelector); }
    }
    private async void RefreshWindowsPlanList()
    {
        long id = ++planRefreshGeneration;
        var originalSelection = windowsPlanSelector.Current;
        try
        {
            var result = await Task.Run(() => { var native = new PowerPlanNative(); return (Plans: native.Enumerate(), Active: native.GetActive()); });
            if (IsDisposed || id != planRefreshGeneration) return;
            windowsPlans = result.Plans;
            RefreshWindowsPlanSelector(Modes.GetCurrent());
            var current = windowsPlanSelector.Current;
            string active = result.Active.Status == 0 ? result.Active.Active.ToString("D") : "읽기 오류:" + result.Active.Status;
            if (windowsPlanSelector.IsCurrent(originalSelection))
                windowsPlanSelector.ShowStatus(current, "현재 OS 계획: " + active + " · 선택 저장 ≠ 실제 적용 · 지금 적용은 별도 버튼");
        }
        catch (Exception ex)
        {
            if (!IsDisposed && id == planRefreshGeneration)
                windowsPlanSelector.ShowStatus(originalSelection, "계획 목록 읽기 실패 (OS 변경 없음): " + ex.Message);
        }
    }
    private void ShowWindowsPlanStatus(PlanSelection selection, string text)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { BeginInvoke(() => ShowWindowsPlanStatus(selection, text)); return; }
        // Revalidate after BeginInvoke too; queued old results cannot replace a newer selection.
        windowsPlanSelector.ShowStatus(selection, text);
    }
}
