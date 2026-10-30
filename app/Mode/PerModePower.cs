namespace GHelper.Mode;

// Values are independent of firmware mode ordinals and CPU boost indices.
internal static class PerModePower
{
    public static string Key(int mode) => "windows_power_mode_" + mode;
    public static int Read(string? value) => int.TryParse(value, out int n) && n >= 0 && n <= 3 ? n : 0;
    // Caller serializes the entire restore/apply transaction with mode transitions.
    public static string Apply(int choice, bool windows, Action restore, Func<Guid, string> set)
    {
        if (windows) restore();
        return choice == 0 ? "unchanged" : set(Overlay(choice));
    }
    public static string ApplyVerified(Guid target, Func<bool> energySaver,
        Func<(uint Status, Guid Value)> readActual, Func<(uint Status, Guid Value)> readEffective,
        Func<Guid, uint> write)
    {
        // Actual records the requested selection; effective records current policy.
        // Neither observation proves physical hardware behavior. Guid.Empty is valid Balanced.
        static (uint Status, Guid Value, string Error) Read(Func<(uint Status, Guid Value)> read)
        {
            try { var value = read(); return (value.Status, value.Value, ""); }
            catch (EntryPointNotFoundException) { return (0, default, "unsupported-api"); }
            catch (DllNotFoundException) { return (0, default, "unsupported-api"); }
            catch (Exception ex) { return (0, default, "read-exception:" + ex.GetType().Name); }
        }
        string Observe(out bool matches)
        {
            // Do not short-circuit: one getter's failure must not hide the other observation.
            var actual = Read(readActual);
            var effective = Read(readEffective);
            matches = false;
            if (actual.Error != "") return actual.Error;
            if (effective.Error != "") return effective.Error;
            if (actual.Status != 0) return "read-error:" + actual.Status;
            if (effective.Status != 0) return "read-error:" + effective.Status;
            matches = actual.Value == target && effective.Value == target;
            return "";
        }
        if (energySaver()) return "energy-saver";
        string error = Observe(out bool beforeMatches);
        if (error != "") return error;
        if (!beforeMatches)
        {
            // Entry is authorized by an explicit choice or persisted association, never default unchanged.
            if (energySaver()) return "energy-saver";
            uint status = write(target);
            if (status != 0) return "write-error:" + status;
        }
        error = Observe(out bool afterMatches);
        if (error != "") return error;
        return afterMatches ? "applied" : "readback-mismatch";
    }
    public static Guid Overlay(int choice) => choice switch
    {
        1 => new("961cc777-2547-4f9d-8174-7d86181b8a7a"),
        2 => Guid.Empty,
        3 => new("ded574b5-45a0-4f42-8737-46345c09c238"),
        _ => throw new ArgumentOutOfRangeException(nameof(choice))
    };
}
