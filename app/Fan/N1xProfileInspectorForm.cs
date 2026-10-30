namespace GHelper.Fan;

internal sealed class N1xProfileInspectorForm : Form
{
    private readonly Button readButton = new() { Text = "제조사 팬 테이블 · 하한 · 전력 원본 읽기 (쓰기 없음)", Dock = DockStyle.Top, Height = 48 };
    private readonly TextBox output = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
        Text = "자동으로 읽거나 적용하지 않습니다. 위 버튼을 눌러 현재 원본을 확인하세요.\r\n수동/커스텀 하드웨어 적용: 안전한 범위·현재 override readback 미확인으로 차단 상태입니다." };
    private bool closed;
    internal N1xProfileInspectorForm(Func<string>? read = null)
    {
        Text = "N1X 제조사 팬·전력 원본 / 적용 미검증";
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(850, 620); MinimumSize = new Size(600, 400);
        Controls.Add(output); Controls.Add(readButton); readButton.SendToBack();
        readButton.Click += async (_, _) =>
        {
            if (!readButton.Enabled || closed) return;
            readButton.Enabled = false;
            try
            {
                string result = await Task.Run(read ?? N1xProfileInspector.Sample);
                if (!closed && !IsDisposed) output.Text = result;
            }
            catch (Exception ex) { if (!closed && !IsDisposed) output.Text = "N/A: " + ex.GetType().Name + ": " + ex.Message; }
            finally { if (!closed && !IsDisposed) readButton.Enabled = true; }
        };
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { closed = true; base.OnFormClosed(e); }
}
