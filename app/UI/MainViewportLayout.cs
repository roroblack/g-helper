namespace GHelper.UI;

/// <summary>Keep top-docked main content reachable when its preferred height exceeds the monitor.</summary>
internal static class MainViewportLayout
{
    internal static void Configure(Form form, Label version, bool scopedCpuRow = false)
    {
        var cpu = scopedCpuRow ? form.Controls.Find("labelCPUFan", true).OfType<Label>().FirstOrDefault() : null;
        var title = cpu?.Parent;
        var perf = title?.Controls.Find("labelPerf", false).FirstOrDefault();
        var icon = title?.Controls.Find("picturePerf", false).FirstOrDefault();
        bool arranging = false;
        int minimumRowHeight = version.Parent?.MinimumSize.Height ?? 0;
        float initialFontSize = version.Font.Size;
        int initialDpi = form.DeviceDpi;
        form.AutoScroll = true;
        void Arrange()
        {
            if (arranging || form.IsDisposed) return;
            arranging = true;
            try
            {
                var working = Screen.FromControl(form).WorkingArea;
                form.MaximumSize = new Size(working.Width, working.Height);
                // Fill uses the space left by the optional right-docked action buttons.
                // A long localized/update label may still wrap: grow its row, never shrink its font.
                int width = Math.Max(1, version.Width - version.Padding.Horizontal);
                var measured = TextRenderer.MeasureText(version.Text, version.Font,
                    new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                var row = version.Parent;
                if (row != null)
                    row.MinimumSize = new Size(row.MinimumSize.Width, Math.Max(
                        (int)Math.Ceiling(minimumRowHeight * (double)version.Font.Size / initialFontSize * form.DeviceDpi / initialDpi),
                        measured.Height + version.Padding.Vertical + row.Padding.Vertical));
                if (cpu != null && title != null && perf != null && icon != null)
                {
                    // Reserve a separate full-width telemetry line below the mode heading.
                    // Measure the actual current font and available width, including wraps.
                    cpu.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                    int top = Math.Max(perf.Bottom, icon.Bottom);
                    int cpuWidth = Math.Max(1, title.ClientSize.Width);
                    var textSize = TextRenderer.MeasureText(cpu.Text, cpu.Font,
                        new Size(cpuWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    cpu.SetBounds(0, top, cpuWidth, Math.Max(1, textSize.Height + cpu.Padding.Vertical));
                    title.Height = cpu.Bottom;
                }
                // WinForms excludes Top-docked children from implicit scroll extents.
                // Sum their real laid-out heights, independent of the current scroll offset.
                int content = form.Padding.Vertical;
                foreach (Control child in form.Controls)
                    if (child.Visible && child.Dock == DockStyle.Top) content += child.Height;
                form.AutoScrollMinSize = new Size(0, content);
            }
            finally { arranging = false; }
        }
        form.Layout += (_, _) => Arrange();
        form.Shown += (_, _) => Arrange();
        form.DpiChanged += (_, _) => Arrange();
        version.TextChanged += (_, _) => Arrange();
        version.FontChanged += (_, _) => Arrange();
        if (cpu != null)
        {
            cpu.TextChanged += (_, _) => Arrange();
            cpu.FontChanged += (_, _) => Arrange();
            title!.SizeChanged += (_, _) => Arrange();
        }
        Arrange();
    }
}
