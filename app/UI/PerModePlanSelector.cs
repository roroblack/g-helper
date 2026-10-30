using GHelper.Mode;
namespace GHelper.UI;

internal readonly record struct PlanSelection(int Mode, Guid? Plan, long Generation, bool Enabled = false);
internal sealed class PerModePlanSelector : UserControl
{
    private readonly Label title = new() { AutoSize = true, Dock = DockStyle.Top };
    internal readonly ComboBox Choice = new() { Name = "windowsPowerPlan", Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
    internal readonly CheckBox EnableAssociation = new() { Text = "이 모드의 전원 관리 계획 전환 사용", AutoSize = true, Dock = DockStyle.Top };
    internal readonly Label Explanation = new() { Text = "선택 저장 ≠ 실제 적용. 사용을 체크하면 이후 이 모드 진입·전원 변경·앱 재실행에도 저장한 계획 연결을 사용합니다. 지금 적용은 아래 별도 버튼입니다. 해제/변경하지 않음은 이전 OS 값을 되돌리지 않습니다.", AutoSize = true, Dock = DockStyle.Top };
    internal readonly Button ApplyButton = new() { Text = "저장한 전원 관리 계획 지금 적용", Enabled = false, AutoSize = true, Dock = DockStyle.Top };
    internal readonly Button RefreshButton = new() { Text = "계획 목록 새로 고침 (읽기 전용)", AutoSize = true, Dock = DockStyle.Top };
    internal readonly Label Status = new() { AutoSize = true, Dock = DockStyle.Top, MaximumSize = new Size(700, 0) };
    private int mode;
    private long generation;
    private Guid? saved;
    private sealed record Item(Guid? Id, string Text) { public override string ToString() => Text; }
    public event Action<PlanSelection>? Saved;
    public event Action<PlanSelection>? ApplyRequested;
    public event Action<PlanSelection>? EnableRequested;
    public event Action? RefreshRequested;
    public PerModePlanSelector()
    {
        AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Dock = DockStyle.Top; Padding = new Padding(4,6,4,6);
        Choice.AccessibleName = "성능 모드별 Windows 전원 관리 계획 GUID (전원 모드와 별개)";
        Controls.Add(Status); Controls.Add(ApplyButton); Controls.Add(RefreshButton); Controls.Add(Explanation); Controls.Add(EnableAssociation); Controls.Add(Choice); Controls.Add(title);
        EnableAssociation.Click += (_, _) => { ++generation; ApplyButton.Enabled = EnableAssociation.Checked && saved.HasValue; Status.Text = "사용 설정 저장 대기 · 아직 적용하지 않음"; EnableRequested?.Invoke(Current); };
        Choice.SelectionChangeCommitted += (_, _) => {
            if (Choice.SelectedItem is not Item item) return;
            saved = item.Id; ++generation;
            ApplyButton.Enabled = EnableAssociation.Checked && saved.HasValue;
            Status.Text = "선택 저장됨 · 아직 적용하지 않음 (변경하지 않음은 되돌리기 아님)";
            Saved?.Invoke(Current);
        };
        ApplyButton.Click += (_, _) => { if (!EnableAssociation.Checked || !saved.HasValue) return; ++generation; Status.Text = "저장된 계획 지금 적용 요청 대기 · 읽기 복귀 확인 전"; ApplyRequested?.Invoke(Current); };
        RefreshButton.Click += (_, _) => RefreshRequested?.Invoke();
    }
    public PlanSelection Current => new(mode, saved, generation, EnableAssociation.Checked);
    public bool IsCurrent(PlanSelection selection) => selection == Current;
    public void ShowMode(int slot, string name, Guid? selected, IReadOnlyList<PowerPlan> plans, bool enabled = false)
    {
        bool changed = slot != mode || saved != selected || EnableAssociation.Checked != enabled;
        mode = slot; saved = selected;
        EnableAssociation.Checked = enabled;
        ApplyButton.Enabled = enabled && selected.HasValue;
        if (changed) ++generation;
        title.Text = $"{name} → Windows 전원 관리 계획 (전원 모드·CPU 부스트와 별개)";
        Choice.BeginUpdate();
        try {
            Choice.Items.Clear(); Choice.Items.Add(new Item(null, "변경하지 않음 (기본값)"));
            foreach (var plan in plans) Choice.Items.Add(new Item(plan.Id, plan.ToString()));
            int index = 0;
            if (selected.HasValue) {
                for (int i=1;i<Choice.Items.Count;i++) if (Choice.Items[i] is Item item && item.Id == selected) index=i;
                if (index==0) { Choice.Items.Add(new Item(selected, $"삭제됨/사용 불가 [{selected:D}]")); index=Choice.Items.Count-1; }
            }
            Choice.SelectedIndex=index;
        } finally { Choice.EndUpdate(); }
        if (changed || Status.Text.Length==0) Status.Text=enabled ? "연결 사용 설정됨 · 지금 적용 결과 아님" : "전환 사용 안 함 (기본값) · 선택 저장 ≠ 실제 적용";
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        int width = Math.Max(1, ClientSize.Width - Padding.Horizontal);
        foreach (Control child in Controls) child.MaximumSize = new Size(width, 0);
        base.OnLayout(e);
    }
    public void ShowStatus(PlanSelection selection, string text) { if (IsCurrent(selection)) Status.Text=text; }
}
