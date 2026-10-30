using GHelper.UI;
using GHelper.Diagnostics.UI;
namespace GHelper;
public partial class Extra
{
    private void InitDiagnostics()
    {
        var section = new FlowLayoutPanel {
            Name = "panelDiagnostics", AccessibleName = "기능 검사",
            Dock = DockStyle.Top, AutoSize = true, WrapContents = true,
            Padding = new Padding(8), TabIndex = 0
        };
        var open = new RButton { Name = "buttonDiagnostics", Text = "기능 검사", AccessibleName = "화면 키보드 포인터 수동 기능 검사", AutoSize = true, Padding = new Padding(8), Secondary = true };
        section.Controls.Add(open);
        var plans = new RButton { Name = "buttonWindowsPlans", Text = "Windows 전원 관리 계획 (선택 사항)…", AutoSize = true, Padding = new Padding(8), Secondary = true };
        plans.Click += (_, _) => Program.settingsForm.ShowWindowsPlanSettings(this);
        section.Controls.Add(plans);
        section.Controls.Add(new Label { AutoSize = true, Text = "화면 · 키보드 · 포인터 (수동 확인 / OEM 인증 아님)", Margin = new Padding(8) });
        // Independent of the service controls; a click opens the UI but starts no test.
        open.Click += (_, _) => {
            using var dialog = new InteractiveDiagnosticsForm([]);
            dialog.ApplyTheme(formBack, foreMain, buttonMain);
            dialog.ShowDialog(this);
        };
        Controls.Add(section);
        Controls.SetChildIndex(section, Controls.Count - 1);
    }
}
