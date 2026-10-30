using GHelper.Mode;

namespace GHelper.UI;

// Shared by the production form and the isolated, hardware-free UI tests.
internal static class PerformanceModeLayout
{
    public static void Configure(TableLayoutPanel table, IReadOnlyList<Control> buttons)
    {
        if (buttons.Count != 6) throw new ArgumentException("Six mode buttons required");
        table.SuspendLayout();
        table.Controls.Clear();
        table.ColumnStyles.Clear();
        table.RowStyles.Clear();
        table.ColumnCount = 3;
        table.RowCount = 2;
        for (int i = 0; i < 3; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
        for (int i = 0; i < 2; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
        for (int i = 0; i < 6; i++)
        {
            buttons[i].Dock = DockStyle.Fill;
            buttons[i].TabIndex = i;
            buttons[i].AccessibleName = buttons[i].Text;
            table.Controls.Add(buttons[i], i % 3, i / 3);
        }
        table.ResumeLayout(true);
    }
}
