namespace GHelper.Mode;
public partial class ModeControl
{
    private long planSelectionGeneration;
    // Exists only while the Windows mode request owns applyGate; never authorizes manual Apply.
    private sealed record PendingPlanTransition(int Target, int Previous, long Request, long Generation,
        long PowerSelection, CancellationToken Token, Guid? Plan, bool Enabled);
    private PendingPlanTransition? pendingPlanTransition;
    private bool IsPendingPlanTransitionCurrent(PendingPlanTransition pending)
    {
        lock (requestSync)
            return pending.Target == Modes.WindowsSlot && pending.Request == requestId &&
                !pending.Token.IsCancellationRequested && Modes.GetCurrent() == pending.Previous &&
                pending.Generation == planSelectionGeneration && pending.PowerSelection == powerSelectionGeneration &&
                pending.Plan == PerModePlan.Read(AppConfig.GetString(PerModePlan.Key(pending.Target))) &&
                pending.Enabled == PlanEnabled(pending.Target);
    }
    private void EnsurePendingPlanTransitionCurrent()
    {
        if (pendingPlanTransition is { } pending && !IsPendingPlanTransitionCurrent(pending))
            throw new InvalidOperationException("Windows mode policy request superseded");
    }
    private bool ApplyWindowsPowerTransition(int mode, int previous, long id, CancellationToken ct)
    {
        // PRECONDITION: production SetPerformanceMode holds applyGate, before ACK-driven commit.
        lock (requestSync)
            pendingPlanTransition = new(mode, previous, id, planSelectionGeneration, powerSelectionGeneration,
                ct, PerModePlan.Read(AppConfig.GetString(PerModePlan.Key(mode))), PlanEnabled(mode));
        try
        {
            EnsurePendingPlanTransitionCurrent();
            ApplyWindowsPower(mode, true);
            lock (requestSync)
            {
                EnsurePendingPlanTransitionCurrent();
                ct.ThrowIfCancellationRequested();
                return CommitMode(id, mode);
            }
        }
        finally { pendingPlanTransition = null; }
    }
    public void SaveWindowsPlan(int mode, Guid? plan)
    {
        lock (requestSync)
        {
            if (mode != Modes.GetCurrent() || !Modes.Exists(mode) || plan == Guid.Empty) return;
            ++planSelectionGeneration;
            AppConfig.Set(PerModePlan.Key(mode), plan?.ToString("D") ?? "");
        }
        Logger.WriteLine($"WindowsPlan saved mode={mode} plan={plan?.ToString("D") ?? "unchanged"}; not applied");
    }
    private static bool PlanEnabled(int mode) => AppConfig.Get(PerModePlan.EnabledKey(mode), 0) == 1;
    // Only explicit user opt-in; saving permission never applies or undoes a plan.
    public async Task SetWindowsPlanEnabled(int mode, bool enabled)
    {
        long id, generation;
        lock (requestSync) { id = requestId; generation = ++planSelectionGeneration; }
        await applyGate.WaitAsync();
        try {
            lock (requestSync) {
                if (id != requestId || generation != planSelectionGeneration || mode != Modes.GetCurrent() || !Modes.Exists(mode)) return;
                AppConfig.Set(PerModePlan.EnabledKey(mode), enabled ? 1 : 0);
            }
            Logger.WriteLine($"WindowsPlan association mode={mode} enabled={enabled}; permission saved only, no OS change/undo");
        }
        finally { applyGate.Release(); }
    }
    public void RequestWindowsPlanApply(int mode, Guid? plan, Action<string> report)
    {
        long id, generation;
        lock (requestSync) { id = requestId; generation = ++planSelectionGeneration; }
        _ = Task.Run(async () => {
            await applyGate.WaitAsync();
            try {
                bool Current() { lock (requestSync) return id == requestId && generation == planSelectionGeneration && mode == Modes.GetCurrent()
                    && PerModePlan.Read(AppConfig.GetString(PerModePlan.Key(mode))) == plan; }
                if (!Current()) return;
                if (!PlanEnabled(mode)) { report("전환 사용 안 함 · OS 변경 없음 (선택 저장 ≠ 실제 적용)"); return; }
                if (!plan.HasValue) { report("변경하지 않음 · 기존 OS 값 되돌리지 않음"); return; }
                // Restore the Windows snapshot first, then explicit plan, then explicit overlay.
                // ApplyWindowsPower uses the same gate and does not acquire it recursively.
                ApplyWindowsPower(mode, mode == Modes.WindowsSlot);
                if (Current()) report("전원 관리 계획 활성 GUID 확인됨 · 후속 모드/전원 변경에도 연결 사용 (하드웨어 효과는 별도)");
            }
            catch (Exception ex) {
                lock (requestSync) {
                    if (id != requestId || generation != planSelectionGeneration || mode != Modes.GetCurrent()) return;
                }
                report("계획/후속 전원 정책 적용 실패 · 저장값 유지: " + ex.Message);
            }
            finally { applyGate.Release(); }
        });
    }
    // PRECONDITION: caller holds applyGate; this helper never creates/deletes/imports plans.
    private void ApplySelectedPowerPlan(int mode)
    {
        if (!PlanEnabled(mode)) return;
        Guid? plan = PerModePlan.Read(AppConfig.GetString(PerModePlan.Key(mode)));
        if (!plan.HasValue) return; // Default unchanged is a true no-I/O path.
        long id, generation;
        lock (requestSync) { id = requestId; generation = planSelectionGeneration; }
        bool Current() { lock (requestSync) return id == requestId && generation == planSelectionGeneration &&
            (mode == Modes.GetCurrent() || (pendingPlanTransition is { } pending && pending.Target == mode &&
                pending.Request == id && pending.Generation == generation && IsPendingPlanTransitionCurrent(pending)))
            && PlanEnabled(mode) && PerModePlan.Read(AppConfig.GetString(PerModePlan.Key(mode))) == plan; }
        var native = new PowerPlanNative(Current); // Checked inside applyGate and again at the setter boundary.
        string result = PerModePlan.Apply(plan, () => native.Enumerate().Select(p => p.Id).ToArray(), native.SetActive, native.GetActive, Current);
        Logger.WriteLine($"WindowsPlan utc={DateTimeOffset.UtcNow:O} mode={mode} plan={plan:D} request={id} generation={generation} enabled={PlanEnabled(mode)} result={result}");
        if (result != "applied" && result != "unchanged") throw new InvalidOperationException("전원 관리 계획: " + result);
    }
}
