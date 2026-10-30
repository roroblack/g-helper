using System;
using System.Drawing;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GHelper.Fan;

namespace GHelper;

internal sealed class NpuClockMain
{
    private readonly Control owner;
    private readonly Func<Task<NpuClockReading>> read;
    private bool busy;
    private bool pending;
    private int visibilityEpoch;
    private int sampleVersion;
    private long sampleStarted;
    private bool hasSample;
    private CancellationTokenSource? queuedRead;
    private sealed class MeasuredLabel : Label
    {
        private void MeasureHeight() => Height = TextRenderer.MeasureText(Text, Font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine).Height + Padding.Vertical;
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); MeasureHeight(); }
        protected override void OnPaddingChanged(EventArgs e) { base.OnPaddingChanged(e); MeasureHeight(); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); MeasureHeight(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); MeasureHeight(); }
    }
    internal Label Label { get; } = new MeasuredLabel() { Name = "labelNpuClock", Text = "NPU clock: N/A", Dock = DockStyle.Top, Font = new Font("Segoe UI", 8.25F), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0) };
    internal NpuClockMain(Control owner, Func<Task<NpuClockReading>> read)
    {
        this.owner = owner;
        this.read = read;
        owner.VisibleChanged += (_, _) =>
        {
            visibilityEpoch++;
            sampleVersion++;
            hasSample = false;
            pending = false;
            queuedRead?.Cancel();
            if (!owner.IsDisposed && !owner.Disposing && !Label.IsDisposed)
            {
                Label.Text = "NPU clock: N/A";
            }
        };
    }

    // Retain the last valid sample only until its acquisition-age deadline.
    internal void Refresh()
    {
        if (owner.IsDisposed || owner.Disposing || !owner.Visible || Label.IsDisposed) return;
        if (!hasSample || Stopwatch.GetElapsedTime(sampleStarted) >= TimeSpan.FromSeconds(3))
        {
            hasSample = false;
            Label.Text = "NPU clock: N/A";
        }
        if (busy) { pending = true; return; }
        Acquire();
    }

    private async void Expire(long started, int version)
    {
        var remaining = TimeSpan.FromSeconds(3) - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        if (version == sampleVersion && !owner.IsDisposed && !owner.Disposing && !Label.IsDisposed)
        {
            hasSample = false;
            Label.Text = "NPU clock: N/A";
        }
    }

    private async void Acquire()
    {
        busy = true;
        pending = false;
        var cancellation = new CancellationTokenSource();
        queuedRead = cancellation;
        try
        {
            var started = Stopwatch.GetTimestamp();
            var epoch = visibilityEpoch;
            var worker = Task.Run(read, cancellation.Token);
            try
            {
                var result = await worker.WaitAsync(TimeSpan.FromSeconds(3));
                if (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(3) && epoch == visibilityEpoch && owner.Visible && !owner.IsDisposed && !owner.Disposing && !Label.IsDisposed)
                {
                    hasSample = result.CurrentMHz is > 0;
                    sampleStarted = started;
                    Label.Text = hasSample ? $"NPU clock: {result.CurrentMHz} MHz" : "NPU clock: N/A";
                    int version = ++sampleVersion;
                    if (hasSample) Expire(started, version);
                }
            }
            catch (TimeoutException)
            {
                if (!owner.IsDisposed && !owner.Disposing && !Label.IsDisposed) { hasSample = false; ++sampleVersion; Label.Text = "NPU clock: N/A"; }
                await worker; // Keep the single-flight owner until the underlying read finishes.
            }
        }
        catch (Exception) { if (!owner.IsDisposed && !owner.Disposing && !Label.IsDisposed) { hasSample = false; ++sampleVersion; Label.Text = "NPU clock: N/A"; } }
        finally
        {
            queuedRead = null;
            cancellation.Dispose();
            busy = false;
            if (pending && owner.Visible && !owner.IsDisposed && !owner.Disposing && !Label.IsDisposed) Acquire();
        }
    }
}
