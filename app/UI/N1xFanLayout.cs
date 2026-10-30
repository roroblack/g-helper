using System.Windows.Forms;

namespace GHelper.UI
{
    internal static class N1xFanLayout
    {
        internal static void ShowUnavailable(Panel footer, Label status, CheckBox apply, Button reset, Button calibrate, TableLayoutPanel charts, CheckBox clamp)
        {
            if (footer.Tag is string marker && marker == "N1xUnavailableLayout") return;
            footer.Tag = "N1xUnavailableLayout";
            clamp.Visible = false;
            var notice = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                Text = "제조사 팬 테이블은 상단의 원본 보기에서 확인하세요.\n검증된 그래프 데이터가 없어 표시하지 않습니다." };
            charts.Controls.Add(notice, 0, 0);
            charts.SetRowSpan(notice, Math.Max(1, charts.RowCount));

            footer.Controls.Clear();
            var stack = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3 };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            status.Anchor = AnchorStyles.Left; status.AutoSize = true;
            apply.Anchor = AnchorStyles.Left; apply.AutoSize = true;
            reset.Anchor = calibrate.Anchor = AnchorStyles.None;
            var actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = true };
            actions.Controls.Add(reset); actions.Controls.Add(calibrate);
            stack.Controls.Add(status, 0, 0); stack.Controls.Add(actions, 0, 1); stack.Controls.Add(apply, 0, 2);
            footer.Controls.Add(stack);
            void UpdateSize(object? sender, EventArgs e) {
                status.MaximumSize = new Size(Math.Max(1, footer.ClientSize.Width - status.Margin.Horizontal), 0);
                footer.Height = stack.GetPreferredSize(new Size(footer.ClientSize.Width, 0)).Height;
            }
            footer.SizeChanged += UpdateSize;
            UpdateSize(null, EventArgs.Empty);
        }
    }
}
