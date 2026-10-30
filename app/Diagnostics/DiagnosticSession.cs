namespace GHelper.Diagnostics;
public enum DiagnosticStatus { NotRun, Running, PassedByUser, FailedByUser, Cancelled }
/// <summary>Interactive functional checks, not vendor certification or automated hardware diagnosis.</summary>
public sealed class DiagnosticSession(string kind, IEnumerable<string> required)
{
    private readonly HashSet<string> required = new(required, StringComparer.Ordinal);
    public string Kind { get; } = kind;
    public DiagnosticStatus Status { get; private set; } = DiagnosticStatus.NotRun;
    public void Start() { if (Status == DiagnosticStatus.NotRun) Status = DiagnosticStatus.Running; }
    public void Cancel() { if (Status is DiagnosticStatus.NotRun or DiagnosticStatus.Running) Status = DiagnosticStatus.Cancelled; }
    public void Fail() { if (Status == DiagnosticStatus.Running) Status = DiagnosticStatus.FailedByUser; }
    public string ExportJson() => System.Text.Json.JsonSerializer.Serialize(new {
        SchemaVersion = 1, Provider = "GHelper.Interactive", VendorCertified = false,
        Kind, Status = Status.ToString(), Required = required.Order().ToArray(), Observed = observed.Order().ToArray(),
        Limitation = "User-observed functional check only; input device attribution and hardware health are not certified."
    }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    private readonly HashSet<string> observed = new(StringComparer.Ordinal);
    public bool Observe(string item) => Status == DiagnosticStatus.Running && required.Contains(item) && observed.Add(item);
    public bool ConfirmPass()
    {
        if (Status != DiagnosticStatus.Running || required.Count == 0 || !required.IsSubsetOf(observed)) return false;
        Status = DiagnosticStatus.PassedByUser;
        return true;
    }
}
