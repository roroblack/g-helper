namespace GHelper.Input;
internal static class CopilotBindings
{
    internal static string? Resolve(string name, string? configured) =>
        !string.IsNullOrEmpty(configured) && configured.Length > 1 ? configured : name switch
        {
            "copilot" => "menu",
            "fncopilot" => "performance",
            _ => configured
        };
}
