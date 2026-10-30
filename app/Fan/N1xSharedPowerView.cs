namespace GHelper.Fan;

// Presentation-only shared scope. Never route this value through a CPU-only API/chart.
internal static class N1xSharedPowerView
{
    internal const string ConfigKey = "overlay_show_cpu_memory_power";
    internal const string Label = "CPU+Memory (W)";
    internal static string Format(N1xTelemetrySnapshot snapshot) => Label + ": " + snapshot.CpuMemoryPower.Display;
    internal static string FormatMain(N1xTelemetrySnapshot snapshot) => Format(snapshot) + "\nGPU draw: " + snapshot.GpuPower.Display + " · GPU voltage: " + snapshot.GpuVoltage.Display;
    // Font size/scale remain fractional; only the final device-pixel extent rounds up.
    internal static int RequiredWidth(string text, float fontSize, float scale, int padding) =>
        checked((int)Math.Ceiling(fontSize * scale * text.Length) + padding * 2);
}
