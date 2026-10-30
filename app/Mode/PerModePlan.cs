namespace GHelper.Mode;

// Traditional plan GUIDs, not overlay IDs, display names or firmware/boost indices.
internal static class PerModePlan
{
    public static string Key(int mode) => "windows_power_plan_" + mode;
    public static string EnabledKey(int mode) => "windows_power_plan_enabled_" + mode;
    public static Guid? Read(string? value) => Guid.TryParseExact(value, "D", out var id) && id != Guid.Empty ? id : null;
    // Called only inside the mode-control gate. A saved GUID alone is not an applied result.
    public static string Apply(Guid? target, Func<IReadOnlyList<Guid>> enumerate, Func<Guid, uint> write,
        Func<(uint Status, Guid Active)> read, Func<bool> current)
    {
        if (!target.HasValue) return "unchanged";
        try
        {
            if (!current()) return "superseded";
            if (!enumerate().Contains(target.Value)) return "missing-plan";
            if (!current()) return "superseded";
            var before = read();
            if (before.Status != 0) return "read-error:" + before.Status;
            if (!current()) return "superseded";
            if (before.Active != target.Value)
            {
                uint error = write(target.Value);
                if (error != 0) return "write-error:" + error;
            }
            var after = read();
            if (!current()) return "superseded";
            if (after.Status != 0) return "read-error:" + after.Status;
            return after.Active == target.Value ? "applied" : "readback-mismatch";
        }
        catch (Exception ex) { return "api-error:" + ex.Message; }
    }
}
