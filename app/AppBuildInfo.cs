namespace GHelper;

/// <summary>Custom release identity, independent from the upstream assembly/update version.</summary>
internal static class AppBuildInfo
{
    public const string ReleaseVersion = "0.16";
    public const string DisplayName = "GHelper H7407BA v" + ReleaseVersion;

    public static string WindowTitle(string model, bool administrator) =>
        DisplayName + (administrator ? " — " : " - ") + model;

    public static string VersionLabel(string upstreamLabel) =>
        DisplayName + " · " + upstreamLabel;

    public static string TrayText(string sensors)
    {
        string text = DisplayName + (string.IsNullOrWhiteSpace(sensors) ? "" : "\n" + sensors);
        // Preserve the complete release identity within the conservative Shell tooltip limit.
        return text.Length <= 63 ? text : text[..63];
    }
}
