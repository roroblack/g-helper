using GHelper.Fan;
namespace GHelper.Fan;
// Presentation only; never feeds hardware control or fan policy.
internal static class OverlayThrmView
{
    internal static string Format(ThermalZoneSample[] zones)
    {
        var matches = zones.Where(x => x.Name == @"\_TZ.THRM").Take(2).ToArray();
        var row = matches.Length == 1 ? matches[0] : null;
        return "THRM (임시): " + (row?.Display ?? "N/A");
    }
}
