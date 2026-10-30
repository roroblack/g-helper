namespace GHelper.Helpers;

internal static class ServiceActionPlan
{
    // Selection is a target list, never an inference from the current Running/Stopped state.
    public static List<string> Select(IEnumerable<string> requested, IEnumerable<string> allowed) =>
        requested.Intersect(allowed, StringComparer.OrdinalIgnoreCase)
            .Where(n => !n.Equals("AsusPTPService", StringComparison.OrdinalIgnoreCase)).ToList();

    public static void Execute(bool start, IEnumerable<string> selected, IReadOnlyDictionary<string, string> states,
        Action<string> startEnable, Action<string> stopDisable, Action stopProArt)
    {
        foreach (string name in selected.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (name == "ProArtProcesses") { if (!start) stopProArt(); continue; }
            if (!states.ContainsKey(name)) continue;
            if (start) startEnable(name); else stopDisable(name);
        }
    }
}
