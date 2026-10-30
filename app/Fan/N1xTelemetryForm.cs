namespace GHelper.Fan;

internal sealed class N1xTelemetryForm : Form
{
    private readonly Func<CancellationToken, N1xTelemetrySnapshot> read;
    private readonly Label[] values = new Label[8];
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(820, 0) };
    private readonly CancellationTokenSource stop = new();
    internal bool PollStopped => stop.IsCancellationRequested;
    internal int PollCount { get; private set; }
    internal N1xTelemetryForm(Func<CancellationToken, N1xTelemetrySnapshot>? read = null, ThermalZoneReader? zoneReader = null)
    {
        this.read = read ?? N1xTelemetryReader.Shared.Sample;
        Text = "N1X Sensors (Read-only)";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(700, 470);
        Size = new Size(960, 860);
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(18), ColumnCount = 3, RowCount = 11 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        string[] names = ["CPU fan", "GPU fan", "CPU temperature", "CPU power", "GPU temperature", "GPU power", "GPU voltage", "CPU+Memory (W)"];
        for (int i = 0; i < names.Length; i++)
        {
            table.Controls.Add(new Label { Text = names[i], AutoSize = true, Margin = new Padding(4, 10, 4, 10) }, 0, i);
            values[i] = new Label { Text = "N/A", AutoSize = true, Margin = new Padding(4, 10, 4, 10) };
            table.Controls.Add(values[i], 1, i);
            table.Controls.Add(new Label { Name = "source" + i, Text = "조회 대기", AutoSize = true, MaximumSize = new Size(450, 0), Margin = new Padding(4, 10, 4, 10) }, 2, i);
        }
        var zones = new ThermalZonePanel(zoneReader ?? ThermalZoneReader.Shared);
        table.Controls.Add(zones, 0, 8); table.SetColumnSpan(zones, 3);
        table.Controls.Add(status, 0, 9); table.SetColumnSpan(status, 3);
        var note = new Label { AutoSize = true, MaximumSize = new Size(820, 0), Text = "2 s refresh. N/A = unavailable, not zero. GPU temperature/voltage: validated NVAPI getters; GPU power: NVML draw, not PL1/PL2 or system power.\nCPU temperature: ASUS DSTS; source/support may be unavailable. CPU-only watts remain N/A until source, units and ABI are verified. CPU+Memory (W): independently validated shared aggregate, HWMonitor-compatible single sample (not 8-sample average); not CPU-only, GPU/module/system watts. Failure/stale samples remain N/A. No GPU/NPU/thermal-zone substitution.", Margin = new Padding(4, 16, 4, 4) };
        table.Controls.Add(note, 0, 10); table.SetColumnSpan(note, 3);
        Controls.Add(table);
        Shown += async (_, _) => { zones.Start(); await PollAsync(); };
        FormClosed += (_, _) => stop.Cancel();
        Disposed += (_, _) => stop.Cancel();
    }

    internal void ApplySnapshot(N1xTelemetrySnapshot s)
    {
        N1xReading[] rows = [s.CpuFan, s.GpuFan, s.CpuTemperature, s.CpuPower, s.GpuTemperature, s.GpuPower, s.GpuVoltage, s.CpuMemoryPower];
        for (int i = 0; i < rows.Length; i++)
        {
            values[i].Text = rows[i].Display;
            Controls[0].Controls["source" + i]!.Text = rows[i].Status;
        }
        status.Text = $"측정: {s.Timestamp.ToLocalTime():HH:mm:ss} · {s.GpuName}";
    }

    private async Task PollAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                ApplySnapshot(N1xTelemetrySnapshot.Missing("refresh pending; previous sample expired"));
                var s = await Task.Run(() => read(stop.Token), stop.Token);
                if (stop.IsCancellationRequested || IsDisposed) return;
                ApplySnapshot(s); PollCount++;
                await Task.Delay(2000, stop.Token); // sequential: never enqueue overlapping polls
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!IsDisposed && !stop.IsCancellationRequested) ApplySnapshot(N1xTelemetrySnapshot.Missing(ex.GetType().Name + ": " + ex.Message));
        }
    }
}
