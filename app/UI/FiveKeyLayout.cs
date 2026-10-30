namespace GHelper.UI;
internal static class FiveKeyLayout
{
    // Reuse the existing binding controls, including their scaled fonts/margins.
    internal static void Apply(TableLayoutPanel table, Control[][] rows)
    {
        table.SuspendLayout();
        table.Controls.Clear();
        table.RowStyles.Clear();
        table.ColumnStyles.Clear();
        table.ColumnCount = 3;
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37.5f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37.5f));
        table.RowCount = rows.Length;
        table.AutoSize = true;
        table.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        table.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
        for (int r = 0; r < rows.Length; r++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int c = 0; c < 3; c++)
            {
                var control = rows[r][c];
                control.Visible = true;
                if (c > 0) control.Dock = DockStyle.Top;
                table.Controls.Add(control, c, r);
            }
        }
        table.ResumeLayout(true);
    }
}
