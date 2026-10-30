using GHelper.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace GHelper.Diagnostics.UI;

/// <summary>Drop-in, no-background-capture interactive checks. Caller opens explicitly via ShowDialog(owner).</summary>
public sealed class InteractiveDiagnosticsForm : Form
{
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { AutoSize = true };
    private readonly TextBox result = new() { Multiline = true, ReadOnly = true, Width = 650, Height = 100, ScrollBars = ScrollBars.Vertical };
    private readonly Dictionary<string, DiagnosticSession> sessions = new();
    private readonly List<string> patterns = ["wordline", "grayscale", "white", "black", "red", "green", "blue"];
    private readonly System.Windows.Forms.Timer ghostTimer = new() { Interval = 100 };
    private long ghostStarted;
    private bool ghostActive, ghostMoved;
    private int patternIndex;
    private readonly List<Keys> expectedKeys;
    private readonly CheckedListBox keySelection = new() { Name = "keySelection", Width = 740, Height = 160, CheckOnClick = true, MultiColumn = true };
    private readonly TextBox keyboardCapture = new() { Name = "keyboardCapture", ReadOnly = true, Width = 740, ShortcutsEnabled = false, Text = "검사 시작 후 여기를 클릭하고 선택한 키를 누르세요." };
    private readonly Label keyFeedback = new() { AutoSize = true, Text = "관찰된 선택 키: 없음" };

    public void ApplyTheme(Color background, Color foreground, Color button)
    {
        void Apply(Control control) {
            control.BackColor = control is System.Windows.Forms.Button ? button : background;
            control.ForeColor = foreground;
            foreach (Control child in control.Controls) Apply(child);
        }
        Apply(this);
        // Pointer canvas intentionally uses contrast, independent of the app theme.
        var area = tabs.Controls.Find("pointerArea", true).FirstOrDefault();
        if (area != null) { area.BackColor = Color.White; area.ForeColor = Color.Black; }
    }

    private void StartCurrent()
    {
        if (tabs.SelectedTab?.Name == "keyboard" && Active.Status == DiagnosticStatus.NotRun) {
            expectedKeys.Clear();
            expectedKeys.AddRange(keySelection.CheckedItems.Cast<Keys>());
            if (expectedKeys.Count == 0) { MessageBox.Show(this, "선택한 키가 없습니다. 검사할 키를 먼저 선택하세요."); return; }
            sessions["keyboard"] = new("keyboard", expectedKeys.Select(k => k.ToString()));
            keySelection.Enabled = false;
            keyboardCapture.Focus();
        }
        Active.Start(); RefreshStatus();
    }

    private void RecordKey(Keys key)
    {
        if (tabs.SelectedTab?.Name != "keyboard" || !keyboardCapture.ContainsFocus || key == Keys.Escape) return;
        if (Active.Observe(key.ToString())) {
            keyFeedback.Text = keyFeedback.Text.Replace("없음", "") + " " + key;
            RefreshStatus();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (tabs.SelectedTab?.Name == "keyboard" && keyboardCapture.ContainsFocus && Active.Status == DiagnosticStatus.Running) {
            RecordKey(keyData & Keys.KeyCode); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    public InteractiveDiagnosticsForm(IEnumerable<Keys> selectedKeys)
    {
        expectedKeys = selectedKeys.Select(k => k & Keys.KeyCode).Where(k => k != Keys.Escape && k != Keys.None).Distinct().ToList();
        Text = "GHelper 대화형 기능 확인 — ASUS 공식 진단 아님";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent; MinimumSize = new Size(600, 480);
        ClientSize = new Size(850, 700); KeyPreview = true;
        Shown += (_, _) => { var work = Screen.FromControl(this).WorkingArea; Size = new Size(Math.Min(Width, work.Width), Math.Min(Height, work.Height)); };
        sessions["display"] = new("display", patterns);
        sessions["keyboard"] = new("keyboard", expectedKeys.Select(k => k.ToString()));
        sessions["pointer"] = new("pointer", ["move", "left", "right", "drag", "line", "ghost-reviewed"]);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 190, AutoScroll = true };
        footer.Controls.Add(status);
        footer.Controls.Add(Button("현재 항목 시작", StartCurrent));
        footer.Controls.Add(Button("사용자 정상 확인", () => { if (!Active.ConfirmPass()) MessageBox.Show(this, "시작 후 모든 관찰을 완료해야 합니다. 자동 하드웨어 판정은 하지 않습니다."); RefreshStatus(); }));
        footer.Controls.Add(Button("사용자 이상 확인", () => { Active.Fail(); RefreshStatus(); }));
        footer.Controls.Add(Button("현재 검사 취소", () => { Active.Cancel(); StopGhost(); RefreshStatus(); }));
        footer.Controls.Add(Button("JSON 내보내기", Export)); footer.Controls.Add(result);
        Controls.Add(tabs); Controls.Add(footer);
        CreateDisplay(); CreateKeyboard(); CreatePointer();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { foreach (var s in sessions.Values) s.Cancel(); Close(); } };
        tabs.SelectedIndexChanged += (_, _) => { StopGhost(); RefreshStatus(); };
        // Losing focus interrupts ghost measurement; it never silently becomes a pass.
        Deactivate += (_, _) => { StopGhost(); };
        FormClosed += (_, _) => { StopGhost(); ghostTimer.Dispose(); foreach (var s in sessions.Values) s.Cancel(); };
        RefreshStatus();
    }

    private DiagnosticSession Active => sessions[tabs.SelectedTab?.Name ?? "display"];
    private static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, AutoSize = true }; b.Click += (_, _) => action(); return b;
    }
    private FlowLayoutPanel Page(string key, string title, string instructions)
    {
        var page = new TabPage(title) { Name = key };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(new Label { Text = instructions, Width = 780, Height = 80 });
        page.Controls.Add(panel); tabs.TabPages.Add(page); return panel;
    }
    private void CreateDisplay()
    {
        var p = Page("display", "화면", "검사 시작 → 패턴 열기. 현재 모니터에서 글자/선, 회색조, 흰색, 검정, RGB를 관찰하세요. 밝기/HDR/색 보정은 변경하지 않습니다. 각 패턴을 보고 창을 닫은 후 관찰 완료를 누르세요. Esc는 언제든 종료합니다.");
        var current = new Label { Text = patterns[0], AutoSize = true }; p.Controls.Add(current);
        bool opened = false;
        p.Controls.Add(Button("패턴 열기", () => {
            if (Active.Status != DiagnosticStatus.Running) return;
            using var canvas = new PatternWindow(patterns[patternIndex], Screen.FromControl(this).Bounds);
            canvas.ShowDialog(this); opened = true;
        }));
        p.Controls.Add(Button("이 패턴 관찰 완료", () => {
            if (!opened || Active.Status != DiagnosticStatus.Running) return;
            Active.Observe(patterns[patternIndex]); opened = false;
            if (patternIndex < patterns.Count - 1) patternIndex++;
            current.Text = patterns[patternIndex]; RefreshStatus();
        }));
    }
    private void CreateKeyboard()
    {
        var p = Page("keyboard", "키보드", "검사 시작 후 이 탭에 포커스를 두고 선택한 키를 누르세요. 선택 키: " + string.Join(", ", expectedKeys) + ". Esc는 취소 전용입니다. Fn, 보안 단축키, 개별 장치/스캔코드 구분, 펌웨어 키는 보장하지 않습니다. 입력 문자는 기록하지 않습니다.");
        p.Controls.Add(new Label { AutoSize = true, Text = "검사할 키를 체크하세요 (기본 선택 없음). 시작 후 선택은 고정됩니다. 다시 검사하려면 창을 다시 여세요." });
        var candidates = Enumerable.Range((int)Keys.A, 26).Concat(Enumerable.Range((int)Keys.D0, 10)).Concat(Enumerable.Range((int)Keys.F1, 12)).Select(k => (Keys)k)
            .Concat(new[] { Keys.Space, Keys.Enter, Keys.Tab, Keys.Back, Keys.Delete, Keys.Insert, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown, Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.ShiftKey, Keys.ControlKey, Keys.Menu, Keys.CapsLock, Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion, Keys.OemMinus, Keys.Oemplus, Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemPipe, Keys.OemSemicolon, Keys.OemQuotes, Keys.Oemtilde }).Concat(expectedKeys).Distinct();
        foreach (var key in candidates) keySelection.Items.Add(key, expectedKeys.Contains(key));
        p.Controls.Add(keySelection); p.Controls.Add(keyboardCapture); p.Controls.Add(keyFeedback);
        KeyDown += (_, e) => { RecordKey(e.KeyCode); if (keyboardCapture.ContainsFocus) e.SuppressKeyPress = true; };
    }
    private void CreatePointer()
    {
        var p = Page("pointer", "포인터/터치패드 수동 확인", "터치패드 전용 센서 검사가 아닙니다. 외장 마우스/터치 입력도 동일 이벤트를 만들 수 있습니다. 검사 시작 후 아래 영역에서 이동·좌우 클릭·누른 채 끌기를 확인하세요. 선 연속성 및 5초 비접촉 관찰은 사용자가 판정합니다. 고스트 자동 합격 없음.");
        var area = new Panel { Name = "pointerArea", Width = 760, Height = 240, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        var points = new List<Point>(); Point? previous = null;
        area.MouseDown += (_, e) => { if (Active.Status != DiagnosticStatus.Running) return; Active.Observe(e.Button == MouseButtons.Left ? "left" : e.Button == MouseButtons.Right ? "right" : "ignored"); previous = e.Location; RefreshStatus(); };
        area.MouseMove += (_, e) => {
            if (Active.Status != DiagnosticStatus.Running) return;
            if (ghostActive) ghostMoved = true;
            if (Active.Observe("move")) RefreshStatus();
            if (e.Button == MouseButtons.Left && previous is Point last && last != e.Location) { Active.Observe("drag"); if (points.Count < 10000) points.Add(e.Location); area.Invalidate(); }
            previous = e.Location;
        };
        area.Paint += (_, e) => { if (points.Count > 1) e.Graphics.DrawLines(Pens.Blue, points.ToArray()); };
        p.Controls.Add(area);
        p.Controls.Add(Button("그려진 선 연속성 확인", () => { if (points.Count > 1) Active.Observe("line"); RefreshStatus(); }));
        var ghostInfo = new Label { Width = 740, Height = 35 }; p.Controls.Add(ghostInfo);
        p.Controls.Add(Button("5초 비접촉 관찰 시작", () => {
            if (Active.Status != DiagnosticStatus.Running) return;
            ghostMoved = false; ghostActive = true; ghostStarted = Environment.TickCount64; ghostInfo.Text = "손을 떼고 관찰하세요. 포커스 이탈 시 중단됩니다."; ghostTimer.Start();
        }));
        ghostTimer.Tick += (_, _) => {
            if (!ghostActive || Environment.TickCount64 - ghostStarted < 5000) return;
            StopGhost();
            var answer = MessageBox.Show(this, ghostMoved ? "관찰 영역에서 이동 이벤트가 있었습니다. 원치 않는 움직임을 직접 확인했습니까?" : "원치 않는 움직임을 직접 확인했습니까? 이벤트가 없다고 자동 정상 판정하지 않습니다.", "비접촉 수동 판정", MessageBoxButtons.YesNoCancel);
            if (answer == DialogResult.Yes) Active.Fail();
            else if (answer == DialogResult.No) Active.Observe("ghost-reviewed");
            ghostInfo.Text = "관찰 종료: " + answer; RefreshStatus();
        };
    }
    private void StopGhost() { ghostActive = false; ghostTimer.Stop(); }
    private void RefreshStatus() {
        string state = Active.Status switch { DiagnosticStatus.NotRun => "미실행", DiagnosticStatus.Running => "진행 중 (사용자 판정 대기)", DiagnosticStatus.PassedByUser => "사용자 정상 확인", DiagnosticStatus.FailedByUser => "사용자 이상 확인", _ => "취소됨" };
        status.Text = tabs.SelectedTab?.Text + ": " + state; result.Text = Active.ExportJson();
    }
    private void Export()
    {
        using var save = new SaveFileDialog { Filter = "JSON|*.json", FileName = "ghelper-interactive.json" };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(save.FileName, "[" + string.Join(",", sessions.Values.Select(s => s.ExportJson())) + "]"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show(this, "저장 실패: " + ex.Message); }
    }
}

internal sealed class PatternWindow : Form
{
    private readonly string pattern;
    internal PatternWindow(string pattern, Rectangle bounds)
    {
        this.pattern = pattern; FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
        Bounds = bounds; KeyPreview = true; DoubleBuffered = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        MouseDown += (_, _) => Close(); Deactivate += (_, _) => Close();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var color = pattern switch { "white" => Color.White, "red" => Color.Red, "green" => Color.Lime, "blue" => Color.Blue, _ => Color.Black };
        e.Graphics.Clear(color);
        if (pattern == "grayscale")
            for (int i = 0; i < 16; i++) { using var b = new SolidBrush(Color.FromArgb(i * 17, i * 17, i * 17)); e.Graphics.FillRectangle(b, i * Width / 16, 0, (Width + 15) / 16, Height); }
        if (pattern == "wordline") {
            for (int x = 0; x < Width; x += 40) e.Graphics.DrawLine(Pens.Gray, x, 0, x, Height);
            for (int y = 0; y < Height; y += 40) e.Graphics.DrawLine(Pens.Gray, 0, y, Width, y);
            e.Graphics.DrawString("Aa 가나다 0123456789 — Esc / 클릭: 닫기", Font, Brushes.White, 20, 20);
        }
    }
}
