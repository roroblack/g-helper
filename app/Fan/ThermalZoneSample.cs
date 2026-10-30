namespace GHelper.Fan;

internal sealed record ThermalZoneSample(string Name, double? Celsius, uint? RawKelvin,
    ulong? ProviderTimestamp, DateTimeOffset AcquiredAtUtc, long AcquiredAtMonotonic, string Status)
{
    public string Scope => "thermal-zone";
    public string PhysicalIdentity => "unknown";
    public string UpstreamFreshness => "unknown";
    public string Display => Celsius is double c ? $"{c:0} °C" : "N/A";
    public string AcquiredTimestamp => AcquiredAtUtc==DateTimeOffset.MinValue ? "N/A" : AcquiredAtUtc.ToString("O");
    public string Details => $"{Name}; WMI thermal zone; raw {RawKelvin?.ToString() ?? "N/A"} K; HWMonitor-compatible K−273.0; {Status}; acquiredAtUtc={AcquiredTimestamp}; Timestamp_Sys100NS={ProviderTimestamp?.ToString() ?? "N/A"}; physicalIdentity=unknown; CPU die 미확정; upstreamFreshness=unknown; firmware cached/fallback possible (THRM LTMP/default 60°C); query freshness ≠ physical freshness";
    internal static ThermalZoneSample Decode(string name, object? raw, object? stamp, DateTimeOffset utc, long mono)
    {
        uint? kelvin = raw is uint k ? k : null;
        bool valid = !string.IsNullOrWhiteSpace(name) && kelvin is >=274 and <=499;
        return new(name, valid ? kelvin - 273.0 : null, kelvin, stamp is ulong ts ? ts : null, utc, mono, valid ? "ok" : "invalid-sample");
    }
}
