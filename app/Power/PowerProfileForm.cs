using System.Globalization;

namespace GHelper.Power;

internal sealed class PowerProfileForm : Form
{
    private readonly PowerProfileStore store;
    private readonly ComboBox slot = new() { Name = "slot", DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ComboBox source = new() { Name = "source", DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly TextBox pl1 = new() { Name = "pl1", Width = 100 };
    private readonly TextBox pl2 = new() { Name = "pl2", Width = 100 };
    private readonly Label status = new() { Name = "status", AutoSize = true, MaximumSize = new Size(680, 0) };
    private int Slot => slot.SelectedIndex == 0 ? 20 : slot.SelectedIndex + 2;
    private bool AC => source.SelectedIndex == 0;
    public PowerProfileForm(string path)
    {
        store = new PowerProfileStore(path);
        Text = "N1X 수동·커스텀 전력 프로필 초안 — 실제 적용 미구현";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(720, 360);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(16) };
        layout.Controls.Add(new Label { Text = "사용자 초안 저장만 지원합니다. 현재값·제조사 기본값·안전 범위가 아닙니다.\nPL1/PL2 단위 및 N1X 실제 backend 미검증: 하드웨어 적용·자동 적용·복원은 수행하지 않습니다.", AutoSize = true, MaximumSize = new Size(680, 0) });
        slot.Items.Add("수동 (슬롯20)");
        for (int i = 3; i <= 19; i++) slot.Items.Add($"커스텀 (슬롯{i})");
        source.Items.AddRange(new object[] { "AC 초안", "DC 초안" });
        var selectors = new FlowLayoutPanel { AutoSize = true }; selectors.Controls.AddRange(new Control[] { slot, source }); layout.Controls.Add(selectors);
        var values = new FlowLayoutPanel { AutoSize = true };
        values.Controls.AddRange(new Control[] { new Label { Text = "PL1 초안", AutoSize = true }, pl1, new Label { Text = "PL2 초안", AutoSize = true }, pl2 }); layout.Controls.Add(values);
        var actions = new FlowLayoutPanel { AutoSize = true };
        var save = new Button { Name = "save", Text = "초안 저장", AutoSize = true };
        var check = new Button { Name = "check", Text = "적용 가능 여부 확인", AutoSize = true };
        var delete = new Button { Name = "delete", Text = "선택 초안 삭제", AutoSize = true };
        save.Click += (_, _) => Run(() =>
        {
            if (!int.TryParse(pl1.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int first) ||
                !int.TryParse(pl2.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int second)) throw new ArgumentException("PL1/PL2는 양의 정수 초안이어야 합니다.");
            store.Save(Slot, AC, first, second);
            status.Text = "초안 저장됨 — 하드웨어 적용 아님. 실효 검증 없음.";
        });
        check.Click += (_, _) => Run(() => status.Text = store.CheckApply(Slot, AC).Reason);
        delete.Click += (_, _) => Run(() =>
        {
            store.RevertDraft(Slot, AC); RefreshDraft();
            status.Text = "선택 초안 삭제됨 — 하드웨어 복원 아님. 하드웨어 변경 없음.";
        });
        actions.Controls.AddRange(new Control[] { save, check, delete }); layout.Controls.Add(actions); layout.Controls.Add(status); Controls.Add(layout);
        slot.SelectedIndexChanged += (_, _) => RefreshDraft(); source.SelectedIndexChanged += (_, _) => RefreshDraft();
        source.SelectedIndex = 0; slot.SelectedIndex = 0;
    }
    private void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
        { status.Text = "프로필 처리 실패: " + ex.Message + " 하드웨어 변경 없음."; }
    }
    private void RefreshDraft()
    {
        pl1.Text = pl2.Text = "";
        if (slot.SelectedIndex < 0 || source.SelectedIndex < 0) return;
        Run(() =>
        {
            var draft = store.Load(Slot, AC);
            pl1.Text = draft?.PL1.ToString(CultureInfo.InvariantCulture) ?? "";
            pl2.Text = draft?.PL2.ToString(CultureInfo.InvariantCulture) ?? "";
            status.Text = draft == null ? "저장된 초안 없음. 기본값 추정 없음." : "저장된 사용자 초안 — 적용 아님. 현재 하드웨어값 아님.";
        });
    }
}
