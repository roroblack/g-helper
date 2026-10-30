using System.Globalization;
namespace GHelper.UI;

internal sealed class PerModePowerSelector : UserControl
{
    private readonly Label title = new() { Dock = DockStyle.Top, AutoSize = true };
    private readonly ComboBox choice = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, Name = "windowsPowerMode" };
    private readonly Label status = new() { Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(700, 0) };
    private readonly ToolTip help = new();
    private int mode;
    private static bool Korean => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";
    public event Action<int, int>? Committed;
    public PerModePowerSelector()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top;
        Padding = new Padding(4, 6, 4, 6);
        choice.Items.AddRange(Korean ? new object[] { "변경하지 않음", "최고의 전력 효율", "균형 조정", "최고의 성능" }
            : new object[] { "Do not change", "Best power efficiency", "Balanced", "Best performance" });
        choice.AccessibleName = Korean ? "성능 모드별 Windows 전원 모드" : "Windows power mode for this performance mode";
        help.SetToolTip(choice, Korean
            ? "현재 성능 모드에만 저장합니다. CPU 부스트/전원 계획과 별개입니다. 현재 AC/배터리 전원에 적용하며 전원 변경 시 다시 적용합니다. 사용자 전원 계획·절전 모드에서는 적용이 제한될 수 있습니다. 변경하지 않음은 시스템 값을 되돌리지 않습니다(윈도우 모드는 기존 OS 정책 복원 유지)."
            : "Saved only for this performance mode. Separate from CPU boost and power plans. Applies to the current AC/battery source and reapplies on source changes. Custom power plans or energy saver may prevent application. Do not change does not undo a previous selection (Windows mode retains OS-policy restoration)." );
        Controls.Add(status); Controls.Add(choice); Controls.Add(title);
        choice.SelectionChangeCommitted += (_, _) => Committed?.Invoke(mode, choice.SelectedIndex);
    }
    public void ShowMode(int id, string name, int selected)
    {
        int next = Math.Clamp(selected, 0, 3);
        bool sameSelection = mode == id && choice.SelectedIndex == next;
        mode = id;
        title.Text = Korean ? $"{name} → Windows 전원 모드" : $"{name} → Windows power mode";
        choice.SelectedIndex = next;
        // A label refresh is not a new transaction. Keep its pending/result/reason.
        // Switching mode or choice invalidates the old result; never cache it per slot.
        if (!sameSelection)
            status.Text = Korean ? "저장된 연결 설정 (실제 적용 상태와 별개)" : "Saved association (not confirmation of application)";
    }
    public void ShowStatus(int id, string text)
    {
        if (mode != id) return;
        status.Text = text switch
        {
            "applied" => Korean ? "저장됨 · Windows 실제 전원 모드 확인됨" : "Saved · effective Windows power mode verified",
            "energy-saver" => Korean ? "저장됨 · 절전 모드 활성: 적용 보류" : "Saved · energy saver active: not applied",
            "unsupported-api" => Korean ? "저장됨 · Windows API 미지원: 적용 안 됨" : "Saved · Windows API unavailable: not applied",
            "readback-mismatch" => Korean ? "저장됨 · 실제 모드 불일치: 적용 확인 실패 (전원 계획 확인)" : "Saved · effective mode mismatch (check power plan)",
            _ when text.StartsWith("read-error:") || text.StartsWith("write-error:") => Korean
                ? "저장됨 · Windows 적용 확인 실패 (전원 계획/권한 확인): " + text
                : "Saved · Windows application failed (check power plan/permissions): " + text,
            _ => text
        };
    }
    protected override void Dispose(bool disposing) { if (disposing) help.Dispose(); base.Dispose(disposing); }
}
