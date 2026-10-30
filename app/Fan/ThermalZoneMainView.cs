namespace GHelper.Fan;

// Presentation only: a zone never enters CPU temperature or fan-control APIs.
internal static class MainThrmView
{
    internal const string Warning = "THRM은 CPU 다이 온도인지 확인되지 않은 임시 대체값입니다. 펌웨어가 60°C 등의 이전 값을 캐시할 수 있습니다.";

    internal static string WithFan(string temperature, string? fan) =>
        temperature.Replace("THRM 대체·캐시 가능", "THRM·임시") +
        (string.IsNullOrWhiteSpace(fan) ? "" : "  " + fan);

    internal static string Format(double? cpuDie, ThermalZoneSample[] zones)
    {
        if (cpuDie is > 0 and < 200 && double.IsFinite(cpuDie.Value))
            return $"CPU: {cpuDie.Value:0} °C";
        var matches = zones.Where(x => x.Name == @"\_TZ.THRM").Take(2).ToArray();
        var row = matches.Length == 1 ? matches[0] : null;
        return "CPU (THRM 대체·캐시 가능): " + (row?.Display ?? "N/A");
    }
}
