using GHelper.Helpers;
using GHelper.UI;

namespace GHelper;
public partial class Extra
{
    private readonly RButton buttonStartServices = new();
    private readonly ServiceActionGate serviceGate = new();
    private bool serviceActionBusy => serviceGate.Busy;
    private void InitServiceActionButtons()
    {
        buttonStartServices.Name = "buttonStartServices";
        buttonStartServices.Text = "시작";
        buttonServices.Text = "중지";
        var actions = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 240,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        foreach (var b in new[] { buttonStartServices, buttonServices })
        {
            b.Anchor = AnchorStyles.None;
            b.Dock = DockStyle.None;
            b.Size = new Size(110, 44);
            b.Margin = new Padding(4, 10, 4, 4);
            b.AccessibleName = b.Text;
            actions.Controls.Add(b);
        }
        panelServices.Controls.Add(actions);
        actions.BringToFront();
        buttonStartServices.Click += (_, _) => RequestServiceAction(true);
    }
    private void RequestServiceAction(bool start)
    {
        if (serviceActionBusy) return;
        var selected = AsusService.OptionalNames.Where(AsusService.IsEnabled).ToArray();
        if (selected.Length == 0) { labelServices.Text = "선택된 대상 없음 — 변경하지 않았습니다."; return; }
        if (ProcessHelper.IsUserAdministrator()) ApplyServiceAction(start, selected);
        else
        {
            if (!serviceGate.TryEnter()) return;
            buttonServices.Enabled = buttonStartServices.Enabled = false;
            ProcessHelper.RunAsAdmin((start ? "services-start " : "services-stop ") + string.Join(",", selected));
            serviceGate.Exit();
            buttonServices.Enabled = buttonStartServices.Enabled = true;
        }
    }
    public async void ApplyServiceAction(bool start, IEnumerable<string> requested)
    {
        if (serviceActionBusy) return;
        var selected = ServiceActionPlan.Select(requested, AsusService.OptionalNames);
        if (selected.Count == 0) { labelServices.Text = "선택된 대상 없음 — 변경하지 않았습니다."; return; }
        if (!serviceGate.TryEnter()) return;
        buttonServices.Enabled = buttonStartServices.Enabled = false;
        foreach (var cb in serviceOptionChecks) cb.Enabled = false;
        labelServices.Text = start ? "선택한 대상 시작 중 ..." : "선택한 대상 중지 중 ...";
        try
        {
            await Task.Run(() => AsusService.ApplyAction(start, selected));
            Program.inputDispatcher?.Init();
        }
        catch (Exception ex) { Logger.WriteLine("Service action failed: " + ex.Message); }
        finally
        {
            serviceGate.Exit();
            if (!IsDisposed)
            {
                foreach (var cb in serviceOptionChecks) cb.Enabled = true;
                InitServices();
            }
        }
    }
}
