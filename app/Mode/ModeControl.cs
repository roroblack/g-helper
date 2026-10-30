using GHelper.Gpu.NVidia;
using GHelper.Helpers;
using GHelper.USB;
using PawnIO;

namespace GHelper.Mode
{
    public partial class ModeControl
    {

        static SettingsForm settings = Program.settingsForm;

        private static bool customFans = false;
        private static int customPower = 0;

        private readonly WindowsModeState windowsState;
        private void CaptureWindowsPolicy()
        {
            // Read everything first. A failed getter must never poison the original snapshot.
            bool keepPlan = windowsState?.Active == true && PlanEnabled(Modes.WindowsSlot) && PerModePlan.Read(AppConfig.GetString(PerModePlan.Key(Modes.WindowsSlot))).HasValue;
            string plan = keepPlan ? AppConfig.GetString("windows_os_plan") : PowerNative.GetPowerPlan();
            bool keepOverlay = windowsState?.Active == true && PerModePower.Read(AppConfig.GetString(PerModePower.Key(Modes.WindowsSlot))) != 0;
            string overlay = keepOverlay ? AppConfig.GetString("windows_os_overlay") : PowerNative.GetOSOverlay();
            int ac = PowerNative.GetOSBoost(false);
            int dc = PowerNative.GetOSBoost(true);
            if (ac < 0 || dc < 0 || overlay is null) throw new InvalidOperationException("Windows policy capture incomplete");
            AppConfig.Set("windows_os_plan", plan);
            AppConfig.Set("windows_os_overlay", overlay);
            AppConfig.Set("windows_os_boost", ac);
            AppConfig.Set("windows_os_boost_dc", dc);
        }
        private void RestoreWindowsPolicy()
        {
            PowerNative.RestoreOSPolicy(AppConfig.GetString("windows_os_plan"),
                AppConfig.GetString("windows_os_overlay"), AppConfig.Get("windows_os_boost"), AppConfig.Get("windows_os_boost_dc")).EnsureSuccess();
        }

        private int _cpuUV = 0;
        private int _igpuUV = 0;
        private int _cpuTemp = CpuInfo.DefaultTemp;
        private bool _ryzenPower = false;

        private static RyzenSmuService? _smu;
        private static readonly object _smuLock = new();

        private static RyzenSmuService? GetSmu()
        {
            lock (_smuLock)
            {
                if (_smu != null && _smu.IsInitialized) return _smu;
                _smu?.Dispose();
                _smu = new RyzenSmuService();
                if (!_smu.Initialize(System.Reflection.Assembly.GetExecutingAssembly()))
                {
                    _smu.Dispose();
                    _smu = null;
                }
                else
                {
                    Logger.WriteLine($"SMU Init: {_smu.CpuCodeName} ({_smu.Family}), SMU v{_smu.SmuVersion >> 16}.{(_smu.SmuVersion >> 8) & 0xFF}.{_smu.SmuVersion & 0xFF}");
                }
                return _smu;
            }
        }

        public static bool IsPawnAvailable()  => GetSmu() != null;
        public static bool IsPawnInstalled()   => RyzenSmuService.IsPawnInstalled();

        static System.Timers.Timer? reapplyTimer;
        static System.Timers.Timer modeToggleTimer = default!;
        static CancellationTokenSource _modeCts = new();
        static Task _modeTask = Task.CompletedTask;
        static readonly SemaphoreSlim applyGate = new(1, 1);
        private readonly object requestSync = new();
        private long requestId;
        public string ModeStatus { get; private set; } = "실제 효과 미검증";
        public bool IsCurrentRequest(long id) { lock (requestSync) return id == requestId; }
        private void PublishModeStatus(long id, int mode, string text, bool refresh = false)
        {
            lock (requestSync)
            {
                if (id != requestId) return;
                ModeStatus = text;
            }
            settings.ShowModeRequest(id, mode, text, refresh);
        }
        private bool CommitMode(long id, int mode)
        {
            lock (requestSync)
            {
                if (id != requestId) return false;
                Modes.SetCurrent(mode); // Persist only after the backend ACK, never a requested selection.
                return true;
            }
        }

        private void ScheduleModeWork(TimeSpan delay, Action work)
        {
            long id;
            CancellationToken ct;
            lock (requestSync) { id = requestId; ct = _modeCts.Token; }
            _ = Task.Run(async () =>
            {
                bool held = false;
                try
                {
                    await Task.Delay(delay, ct);
                    await applyGate.WaitAsync(ct);
                    held = true;
                    ct.ThrowIfCancellationRequested();
                    if (IsCurrentRequest(id) && Modes.GetCurrent() != Modes.WindowsSlot) work();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { Logger.WriteLine("Deferred mode work failed: " + ex.Message); }
                finally { if (held) applyGate.Release(); }
            });
        }

        public ModeControl()
        {
            windowsState = new WindowsModeState(CaptureWindowsPolicy, RestoreWindowsPolicy,
                () =>
                {
                    int status = Program.acpi.SetPerformanceMode(AsusACPI.PerformanceBalanced);
                    if (status != 1) throw new InvalidOperationException($"Windows baseline rejected: {status}");
                });
            if (AppConfig.GetString("windows_os_plan") is null)
            {
                try { CaptureWindowsPolicy(); }
                catch (Exception ex) { Logger.WriteLine("Windows snapshot unavailable; Windows restore will fail closed: " + ex.Message); }
            }
            int reapplyTime = AppConfig.Get("reapply_time", IsReapplyTempRequired() ? 30 : 0);
            if (reapplyTime > 0)
            {
                reapplyTimer = new System.Timers.Timer(reapplyTime * 1000);
                reapplyTimer.Elapsed += ReapplyTimer_Elapsed;
            }
        }

        // Cezanne/Rembrandt (Renoir) + Phoenix/HawkPoint (Mobile) silently reset temp limit under load.
        private static bool IsReapplyTempRequired()
        {
            var smu = GetSmu();
            return smu != null && smu.Family is CpuFamily.Renoir or CpuFamily.Mobile;
        }

        private static bool IsReapplyRyzenRequired()
        {
            var smu = GetSmu();
            return smu != null && smu.Family is CpuFamily.Raphael;
        }

        private static void SetReapplyEnabled(bool enabled)
        {
            if (reapplyTimer != null) reapplyTimer.Enabled = enabled;
        }


        private void ReapplyTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            if (Modes.GetCurrent() == Modes.WindowsSlot) return;
            ScheduleModeWork(TimeSpan.Zero, () =>
            {
                SetCPUTemp(AppConfig.GetMode("cpu_temp"));
                SetRyzenPower();
            });
        }

        public void WaitForApply()
        {
            try { _modeTask.Wait(5000); } catch { }
        }

        public void AutoPerformance(bool powerChanged = false)
        {
            int mode = AppConfig.Get("performance_" + Program.PerformanceKey());
            Logger.WriteLine($"{Program.currentSource} Performance Mode: {Modes.GetName(mode == -1 ? Modes.GetCurrent() : mode)}");

            if (mode != -1)
                SetPerformanceMode(mode, powerChanged);
            else
                SetPerformanceMode(Modes.GetCurrent());
        }


        public void ResetPerformanceMode()
        {
            if (Modes.GetCurrent() == Modes.WindowsSlot) return;
            ResetRyzen();

            // Reapply through the serialized, ACK-checked transition only.
            SetPerformanceMode();
        }

        public void Toast()
        {
            Program.toast.RunToast(Modes.GetCurrentName(), SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Online ? ToastIcon.Charger : ToastIcon.Battery);
        }

        public void SetPerformanceMode(int mode = -1, bool notify = false)
        {
            if (mode < 0) mode = Modes.GetCurrent();
            if (!Modes.Exists(mode)) mode = 0;
            long id;
            CancellationToken ct;
            lock (requestSync)
            {
                id = ++requestId;
                // The requested mode is not the persisted/accepted mode.
                _modeCts.Cancel();
                _modeCts = new CancellationTokenSource();
                ct = _modeCts.Token;
            }
            PublishModeStatus(id, mode, "요청됨 · 펌웨어 효과 미확인");
            _modeTask = Task.Run(async () =>
            {
                bool gateHeld = false;
                bool accepted = false;
                try
                {
                    await applyGate.WaitAsync(ct);
                    gateHeld = true;
                    ct.ThrowIfCancellationRequested();
                    if (!IsCurrentRequest(id)) return;
                    int oldMode = Modes.GetCurrent();
                    PublishModeStatus(id, mode, "적용 요청 중 · 펌웨어 효과 미확인");
                    bool reset = AppConfig.IsResetRequired() && (Modes.GetBase(oldMode) == Modes.GetBase(mode)) && customPower > 0 && !AppConfig.IsApplyPower();
                    customFans = false;
                    customPower = 0;
                    if (mode == Modes.WindowsSlot)
                    {
                        SetReapplyEnabled(false);
                        if (!ApplyWindowsPowerTransition(mode, oldMode, id, ct)) return; // Policy success precedes atomic guarded commit.
                        accepted = true;
                    }
                    else
                    {
                        windowsState.Leave();
                        if (reset)
                        {
                            int resetStatus = Program.acpi.DeviceSet(AsusACPI.PerformanceMode, (Modes.GetBase(oldMode) != 1) ? AsusACPI.PerformanceTurbo : AsusACPI.PerformanceBalanced, "ModeReset");
                            if (resetStatus != 1) throw new InvalidOperationException($"Mode reset rejected: {resetStatus}");
                            await Task.Delay(TimeSpan.FromMilliseconds(1500), ct);
                        }
                        ct.ThrowIfCancellationRequested();
                        // Byte-verified H7407BA vendor request4 uses thermal1 before separate curves.
                        // Exact observed model/SKU only; broad write guards are not mapping evidence.
                        // Baseline-only correction: no fan writes or claim of full vendor Manual parity.
                        bool manualBaseline = AsusACPI.N1xRestricted && mode == Modes.ManualSlot &&
                            (string.Equals(AppConfig.GetModel(), "ProArt P14 H7407BA", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(AppConfig.GetModel(), "H7407BA", StringComparison.OrdinalIgnoreCase));
                        int thermalMode = manualBaseline ? AsusACPI.PerformanceTurbo :
                            !AsusACPI.N1xRestricted && AppConfig.IsManualModeRequired() ? AsusACPI.PerformanceManual : Modes.GetBase(mode);
                        int status = Program.acpi.SetPerformanceMode(thermalMode);
                        if (status != 1) throw new InvalidOperationException($"Firmware request rejected: {status}");
                        accepted = true;
                        ct.ThrowIfCancellationRequested();
                        if (!CommitMode(id, mode)) return;
                        ApplyOSOverrides(mode);
                        if (AppConfig.Is("status_mode")) Program.acpi.DeviceSet(AsusACPI.StatusMode, [0x00, Modes.GetBase(mode) == AsusACPI.PerformanceSilent ? (byte)0x02 : (byte)0x03], "StatusMode");
                        SetGPUClocksCore(); // Synchronous inside applyGate: no detached old-mode GPU task.
                        await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                        AutoFans();
                        await Task.Delay(TimeSpan.FromMilliseconds(1000), ct);
                        ct.ThrowIfCancellationRequested();
                        AutoPower();
                        var command = AppConfig.GetModeString("mode_command");
                        if (command is not null)
                        {
                            Logger.WriteLine("Running mode command: " + command);
                            RestrictedProcessHelper.RunAsRestrictedUser(command);
                        }
                    }
                    ct.ThrowIfCancellationRequested();
                    const string result = "펌웨어 요청 수락 · 실제 효과 미검증";
                    Logger.WriteLine($"Mode {mode}: firmware request accepted; effective unverified (not six distinct firmware slots)");
                    PublishModeStatus(id, mode, result, true);
                    // A successful ACK is not a physical-mode readback. No success toast claiming otherwise.
                }
                catch (OperationCanceledException)
                {
                    Logger.WriteLine($"SetPerformanceMode superseded (mode {mode}); effective state unverified");
                }
                catch (Exception ex)
                {
                    string result = (accepted ? "펌웨어 요청 수락; 후속 정책 실패 · 효과 미확인: " : "모드 요청 실패 · 이전 선택 유지; 실제 효과 미확인: ") + ex.Message;
                    Logger.WriteLine($"SetPerformanceMode failed (mode {mode}): {result}");
                    PublishModeStatus(id, Modes.GetCurrent(), result, true);
                }
                finally { if (gateHeld) applyGate.Release(); }
            });
        }


        private void ApplyWindowsPower(int mode, bool restore = false)
        {
            int choice = PerModePower.Read(AppConfig.GetString(PerModePower.Key(mode)));
            long id, selection;
            lock (requestSync) { id = requestId; selection = powerSelectionGeneration; }
            string outcome = "pending";
            AuditWindowsPower("before", mode, choice, id, selection, outcome);
            try
            {
                if (restore) windowsState.Enter();
                EnsurePendingPlanTransitionCurrent();
                ApplySelectedPowerPlan(mode);
                EnsurePendingPlanTransitionCurrent();
                string result = PerModePower.Apply(choice, false, windowsState.Enter, PowerNative.ApplyOverlayOnly);
                outcome = result;
                string text = result == "unchanged"
                    ? (mode == Modes.WindowsSlot ? "OS 정책 복원 유지 · 전원 모드 추가 변경 없음" : "전원 모드 변경하지 않음")
                    : result;
                Logger.WriteLine($"WindowsPower mode={mode} choice={choice}: {text}");
                PublishWindowsPowerStatus(mode, choice, id, selection, text);
                if (result != "unchanged" && result != "applied") throw new InvalidOperationException(text);
            }
            catch (Exception ex)
            {
                if (outcome == "pending") outcome = "exception:" + ex.GetType().Name + ":" + ex.Message;
                string text = "전원 모드 적용 실패 (설정은 보관됨): " + ex.Message;
                Logger.WriteLine($"WindowsPower mode={mode}: {text}");
                PublishWindowsPowerStatus(mode, choice, id, selection, text);
                throw;
            }
            finally { AuditWindowsPower("after", mode, choice, id, selection, outcome); }
        }

        private long powerSelectionGeneration;
        private bool IsCurrentPowerSelection(int mode, int choice, long id, long selection)
        {
            lock (requestSync)
                return requestId == id && powerSelectionGeneration == selection &&
                    PerModePower.Read(AppConfig.GetString(PerModePower.Key(mode))) == choice;
        }
        private void PublishWindowsPowerStatus(int mode, int choice, long id, long selection, string text)
            => settings.ShowWindowsPowerStatus(mode, text, () => IsCurrentPowerSelection(mode, choice, id, selection));

        private void AuditWindowsPower(string phase, int mode, int choice, long id, long selection, string outcome)
        {
            // Observability must never replace the setter/restore outcome or add writes/retries.
            try
            {
                string target = choice == 0 ? "unchanged" : PerModePower.Overlay(choice).ToString("D");
                Logger.WriteLine($"WindowsPowerAudit utc={DateTimeOffset.UtcNow:O} phase={phase} mode={mode} choice={choice} request={id} selection={selection} target={target} outcome={outcome} current={IsCurrentPowerSelection(mode, choice, id, selection)} {PowerNative.ReadOverlayAudit()}");
            }
            catch (Exception) { /* Diagnostic failure must not mask the original outcome. */ }
        }

        public void SelectWindowsPower(int mode, int choice)
        {
            if (mode != Modes.GetCurrent() || choice < 0 || choice > 3) return;
            AppConfig.Set(PerModePower.Key(mode), choice);
            long id, selection;
            lock (requestSync) { id = requestId; selection = ++powerSelectionGeneration; }
            AuditWindowsPower("commit", mode, choice, id, selection, "saved-not-applied");
            PublishWindowsPowerStatus(mode, choice, id, selection, "선택 저장됨 · 적용 대기");
            _ = Task.Run(async () =>
            {
                await applyGate.WaitAsync();
                try
                {
                    // The newest stored choice still owns the gate; stale UI outcomes do not.
                    if (!IsCurrentRequest(id) || mode != Modes.GetCurrent())
                    {
                        AuditWindowsPower("skipped", mode, choice, id, selection, "superseded-mode-request");
                        return;
                    }
                    ApplyWindowsPower(mode, mode == Modes.WindowsSlot);
                }
                catch (Exception ex) { Logger.WriteLine("Windows power selection failed: " + ex.Message); }
                finally { applyGate.Release(); }
            });
        }

        private void ApplyOSOverrides(int mode)
        {
            // No implicit overlay or power-plan switch: every mode defaults to unchanged.
            // Legacy powermode/scheme keys are retained on disk but no longer override this selector.
            if (!AppConfig.Is("skip_powermode"))
            {
                if (AppConfig.IsAutoASPM()) PowerNative.SetBalancedASPM();
                if (AppConfig.IsAutoStandbyNetworking()) PowerNative.SetConnectivityInStandby();
            }
            // CPU Boost remains independent; use the captured mode, not a mutable current-mode key.
            int boost = AppConfig.Get("auto_boost_" + mode);
            if (mode != Modes.WindowsSlot && boost != -1) PowerNative.SetCPUBoost(boost);
            // Verify the requested overlay after any power-plan reactivation by existing controls.
            ApplyWindowsPower(mode);
        }

        private int pendingCycleMode = -1;
        private void ModeToggleTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            modeToggleTimer.Stop();
            int mode;
            lock (requestSync) { mode = pendingCycleMode; pendingCycleMode = -1; }
            if (mode >= 0) SetPerformanceMode(mode, true);
        }

        public void CyclePerformanceMode(bool back = false)
        {
            int delay = AppConfig.Get("mode_delay", 1000);
            if (modeToggleTimer is null)
            {
                modeToggleTimer = new System.Timers.Timer(delay);
                modeToggleTimer.Elapsed += ModeToggleTimer_Elapsed;
            }
            modeToggleTimer.Stop();
            lock (requestSync)
            {
                var list = Modes.GetList();
                int index = list.IndexOf(pendingCycleMode >= 0 ? pendingCycleMode : Modes.GetCurrent());
                pendingCycleMode = list[(index + (back ? list.Count - 1 : 1)) % list.Count];
            }
            modeToggleTimer.Start(); // Debounce only; never persist a not-yet-acknowledged selection.
        }

        public void AutoFans(bool force = false)
        {
            if (Modes.GetCurrent() == Modes.WindowsSlot) return;
            customFans = false;
            if (AsusACPI.N1xRestricted)
            {
                SetModeLabel();
                return; // Block forced curves, fallback ranges and deferred power fixes too.
            }

            if (AppConfig.IsApplyFans() || force)
            {

                bool xgmFan = false;
                if (AppConfig.Is("xgm_fan"))
                {
                    XGM.SetFan(AppConfig.GetFanConfig(AsusFan.XGM));
                    xgmFan = Program.acpi.IsXGConnected();
                }

                int cpuResult = Program.acpi.SetFanCurve(AsusFan.CPU, AppConfig.GetFanConfig(AsusFan.CPU));
                int gpuResult = Program.acpi.SetFanCurve(AsusFan.GPU, AppConfig.GetFanConfig(AsusFan.GPU));

                if (AppConfig.Is("mid_fan"))
                    Program.acpi.SetFanCurve(AsusFan.Mid, AppConfig.GetFanConfig(AsusFan.Mid));


                // Alternative way to set fan curve
                if (cpuResult != 1 || gpuResult != 1)
                {
                    cpuResult = Program.acpi.SetFanRange(AsusFan.CPU, AppConfig.GetFanConfig(AsusFan.CPU));
                    gpuResult = Program.acpi.SetFanRange(AsusFan.GPU, AppConfig.GetFanConfig(AsusFan.GPU));

                    // Something went wrong, resetting to default profile
                    if (cpuResult != 1 || gpuResult != 1)
                    {
                        Program.acpi.DeviceSet(AsusACPI.PerformanceMode, Modes.GetCurrentBase(), "Reset Mode");
                        settings.LabelFansResult("Model doesn't support custom fan curves");
                    }
                }
                else
                {
                    settings.LabelFansResult("");
                    customFans = true;
                }

                int hystUp = AppConfig.GetMode("hysteresis_up");
                int hystDown = AppConfig.GetMode("hysteresis_down");
                if (hystUp > 0 && hystDown > 0)
                    Program.acpi.SetFanHysteresis(hystUp, hystDown);

                // force set PPTs for missbehaving bios on FX507/517 series
                if ((AppConfig.IsPowerRequired() || xgmFan) && !AppConfig.IsApplyPower())
                {
                    ScheduleModeWork(TimeSpan.FromSeconds(1), () =>
                    {
                        Program.acpi.DeviceSet(AsusACPI.PPT_APUA0, 80, "PowerLimit Fix A0");
                        Program.acpi.DeviceSet(AsusACPI.PPT_APUA3, 80, "PowerLimit Fix A3");
                    });
                }

            } else
            {
                XGM.Reset();
            }

            SetModeLabel();

        }

        public void AutoPower(bool launchAsAdmin = false)
        {
            if (Modes.GetCurrent() == Modes.WindowsSlot) return;
            if (AsusACPI.N1xRestricted) { customPower = 0; SetModeLabel(); return; }

            customPower = 0;

            bool applyPower = AppConfig.IsApplyPower();
            bool applyFans = AppConfig.IsApplyFans();

            if (applyPower && !applyFans && AppConfig.IsFanRequired())
            {
                AutoFans(true);
                Thread.Sleep(500);
            }

            if (applyPower) SetPower(launchAsAdmin);

            Thread.Sleep(500);
            SetGPUPower();
            if (applyPower) SetCrossPower();
            AutoRyzen();

            if (IsReapplyRyzenRequired())
                ScheduleModeWork(TimeSpan.FromSeconds(5), () => { AutoRyzen(); ReadRyzenLimits(); });

        }

        public void SetModeLabel()
        {
            settings.SetModeLabel(Properties.Strings.PerformanceMode + ": " + Modes.GetCurrentName() + (customFans ? "+" : "") + ((customPower > 0) ? " " + customPower + "W" : "" ) + " · " + ModeStatus);
        }

        public void SetRyzenPower(bool init = false)
        {
            if (init) _ryzenPower = true;

            if (!_ryzenPower) return;
            if (!AppConfig.IsApplyPower()) return;

            var smu = GetSmu();
            if (smu == null) return;

            int limit_total = AppConfig.GetMode("limit_total");
            int limit_slow = AppConfig.GetMode("limit_slow", limit_total);
            int limit_fast = AppConfig.GetMode("limit_fast", limit_slow);

            if (limit_total > AsusACPI.MaxTotal) return;
            if (limit_total < AsusACPI.MinTotal) return;

            smu.SetAllLimits(limit_total, limit_fast, limit_slow,
                out SmuStatus stapm, out SmuStatus fast, out SmuStatus slow);
            if (init) Logger.WriteLine($"STAPM: {limit_total}W {stapm} | SLOW: {limit_slow}W {slow} | FAST: {limit_fast}W {fast}");
        }

        public void SetPower(bool launchAsAdmin = false)
        {
            if (AsusACPI.N1xRestricted) { customPower = 0; SetModeLabel(); return; }

            bool allAMD = Program.acpi.IsAllAmdPPT();
            bool isAMD = CpuInfo.IsAMD;

            int limit_total = AppConfig.GetMode("limit_total");
            int limit_cpu = AppConfig.GetMode("limit_cpu");
            int limit_slow = AppConfig.GetMode("limit_slow");
            int limit_fast = AppConfig.GetMode("limit_fast");

            if (limit_slow < 0 || allAMD) limit_slow = limit_total;

            if (limit_total > AsusACPI.MaxTotal) return;
            if (limit_total < AsusACPI.MinTotal) return;

            if (limit_cpu > AsusACPI.MaxCPU) return;
            if (limit_cpu < AsusACPI.MinCPU) return;

            if (limit_fast > AsusACPI.MaxTotal) return;
            if (limit_fast < AsusACPI.MinTotal) return;

            if (limit_slow > AsusACPI.MaxTotal) return;
            if (limit_slow < AsusACPI.MinTotal) return;

            // SPL and SPPT
            if (Program.acpi.IsSupported(AsusACPI.PPT_APUA0))
            {
                Program.acpi.DeviceSet(AsusACPI.PPT_APUA3, limit_total, "PowerLimit A3");
                Program.acpi.DeviceSet(AsusACPI.PPT_APUA0, limit_slow, "PowerLimit A0");
                customPower = limit_total;
            }
            else if (isAMD)
            {
                if (ProcessHelper.IsUserAdministrator())
                {
                    SetRyzenPower(true);
                }
                else if (launchAsAdmin)
                {
                    ProcessHelper.RunAsAdmin("cpu");
                    return;
                }
            }

            if (allAMD) // CPU limit all amd models
            {
                Program.acpi.DeviceSet(AsusACPI.PPT_CPUB0, limit_cpu, "PowerLimit B0");
                customPower = limit_cpu;
            }
            else if (isAMD && Program.acpi.IsSupported(AsusACPI.PPT_APUC1)) // FPPT boost for non all-amd models
            {
                Program.acpi.DeviceSet(AsusACPI.PPT_APUC1, limit_fast, "PowerLimit C1");
            }

            SetModeLabel();

        }

        public void SetCrossPower()
        {
            int gpucpu = AppConfig.GetMode("limit_gpucpu");
            int crossload = AppConfig.GetMode("limit_crossload");
            int cputemp = AppConfig.GetMode("limit_cputemp");

            if (gpucpu >= AsusACPI.MinGPUtoCPU && gpucpu <= AsusACPI.MaxGPUtoCPU && Program.acpi.IsSupported(AsusACPI.PPT_GPUCPU9C))
                Program.acpi.DeviceSet(AsusACPI.PPT_GPUCPU9C, gpucpu, "PowerLimit 9C (GPU to CPU)");

            if (crossload >= AsusACPI.MinCrossLoad && crossload <= AsusACPI.MaxCrossLoad && Program.acpi.IsSupported(AsusACPI.PPT_CROSS9F))
                Program.acpi.DeviceSet(AsusACPI.PPT_CROSS9F, crossload, "PowerLimit 9F (Cross Loading)");

            if (cputemp >= AsusACPI.MinCPUTemp && cputemp <= AsusACPI.MaxCPUTemp && Program.acpi.IsSupported(AsusACPI.PPT_TEMP9E))
                Program.acpi.DeviceSet(AsusACPI.PPT_TEMP9E, cputemp, "PowerLimit 9E (CPU Temp)");
        }

        public void SetGPUClocks(bool launchAsAdmin = true, bool reset = false)
        {
            if (Modes.GetCurrent() == Modes.WindowsSlot && !reset) return;
            long id;
            lock (requestSync) id = requestId;
            _ = Task.Run(async () =>
            {
                await applyGate.WaitAsync();
                try { if (IsCurrentRequest(id)) SetGPUClocksCore(launchAsAdmin, reset); }
                finally { applyGate.Release(); }
            });
        }

        private void SetGPUClocksCore(bool launchAsAdmin = true, bool reset = false)
        {
                if (Modes.GetCurrent() == Modes.WindowsSlot && !reset) return;
                int core = AppConfig.GetMode("gpu_core");
                int memory = AppConfig.GetMode("gpu_memory");
                int clock_limit = AppConfig.GetMode("gpu_clock_limit");

                if (reset) core = memory = clock_limit = 0;

                if (core == -1 && memory == -1 && clock_limit == -1) return;
                //if ((gpu_core > -5 && gpu_core < 5) && (gpu_memory > -5 && gpu_memory < 5)) launchAsAdmin = false;

                if (Program.acpi.DeviceGet(AsusACPI.GPUEco) == 1) { Logger.WriteLine("Clocks: Eco"); return; }
                if (HardwareControl.GpuControl is null) { Logger.WriteLine("Clocks: NoGPUControl"); return; }
                if (!HardwareControl.GpuControl!.IsNvidia) { Logger.WriteLine("Clocks: NotNvidia"); return; }

                NvidiaGpuControl nvControl = (NvidiaGpuControl)HardwareControl.GpuControl;
                try
                {
                    int statusClocks = nvControl.SetClocks(core, memory);
                    int statusLimit = nvControl.SetMaxGPUClock(clock_limit);
                    if ((statusLimit != 0 || statusClocks != 0) && launchAsAdmin) ProcessHelper.RunAsAdmin("gpu");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Clocks Error:" + ex.ToString());
                }

                settings.GPUInit();
        }

        public void SetGPUPower()
        {
            if (Modes.GetCurrent() == Modes.WindowsSlot) return;

            int gpu_boost = AppConfig.GetMode("gpu_boost");
            int gpu_temp = AppConfig.GetMode("gpu_temp");
            int gpu_power = AppConfig.GetMode("gpu_power");

            int boostResult = -1;

            if (gpu_power >= AsusACPI.MinGPUPower && gpu_power <= AsusACPI.MaxGPUPower && Program.acpi.IsSupported(AsusACPI.GPU_POWER))
                Program.acpi.DeviceSet(AsusACPI.GPU_POWER, gpu_power, "PowerLimit TGP (GPU VAR)");

            if (gpu_boost >= AsusACPI.MinGPUBoost && gpu_boost <= AsusACPI.MaxGPUBoost && Program.acpi.IsSupported(AsusACPI.PPT_GPUC0))
                boostResult = Program.acpi.DeviceSet(AsusACPI.PPT_GPUC0, gpu_boost, "PowerLimit C0 (GPU BOOST)");

            if (gpu_temp >= AsusACPI.MinGPUTemp && gpu_temp <= AsusACPI.MaxGPUTemp && Program.acpi.IsSupported(AsusACPI.PPT_GPUC2))
                Program.acpi.DeviceSet(AsusACPI.PPT_GPUC2, gpu_temp, "PowerLimit C2 (GPU TEMP)");

            // Fallback
            if (boostResult == 0)
                Program.acpi.DeviceSet(AsusACPI.PPT_GPUC0, gpu_boost, "PowerLimit C0");

        }

        public SmuStatus? SetCPUTemp(int cpuTemp, bool log = false)
        {
            if (cpuTemp < CpuInfo.MinTemp || cpuTemp > CpuInfo.DefaultTemp) return null;
            if (cpuTemp == CpuInfo.DefaultTemp && _cpuTemp == CpuInfo.DefaultTemp) return null;

            var smu = GetSmu();
            if (smu == null) return null;
            SmuStatus status = smu.SetThm(cpuTemp);
            if (log) Logger.WriteLine($"CPU Temp: {cpuTemp}°C {status}");
            if (status == SmuStatus.OK) _cpuTemp = cpuTemp;
            return status;
        }

        public void SetUV(int cpuUV)
        {
            if (!CpuInfo.IsSupportedUV()) return;

            if (cpuUV >= CpuInfo.MinCPUUV && cpuUV <= CpuInfo.MaxCPUUV)
            {
                var smu = GetSmu();
                if (smu == null) return;
                SmuStatus status = smu.SetCoAll(cpuUV);
                Logger.WriteLine($"UV: {cpuUV} {status}");
                if (status == SmuStatus.OK) _cpuUV = cpuUV;
            }
        }

        public void SetUViGPU(int igpuUV)
        {
            if (!CpuInfo.IsSupportedUViGPU()) return;

            if (igpuUV >= CpuInfo.MinIGPUUV && igpuUV <= CpuInfo.MaxIGPUUV)
            {
                var smu = GetSmu();
                if (smu == null) return;
                SmuStatus status = smu.SetCoGfx(igpuUV);
                Logger.WriteLine($"iGPU UV: {igpuUV} {status}");
                if (status == SmuStatus.OK) _igpuUV = igpuUV;
            }
        }

        public string SetRyzen(bool launchAsAdmin = false)
        {
            if (!ProcessHelper.IsUserAdministrator())
            {
                if (launchAsAdmin) ProcessHelper.RunAsAdmin("uv");
                return string.Empty;
            }

            var smu = GetSmu();
            if (smu == null) return string.Empty;

            var lines = new System.Text.StringBuilder();
            try
            {
                int cpuUV   = AppConfig.GetMode("cpu_uv",   0);
                int igpuUV  = AppConfig.GetMode("igpu_uv",  0);
                int cpuTemp = AppConfig.GetMode("cpu_temp");
                string? cpuUVCores = AppConfig.GetModeString("cpu_uv_cores");

                if (CpuInfo.IsSupportedUV() && cpuUVCores is not null)
                {
                    int core = 0;
                    foreach (var token in cpuUVCores.Split('-', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(token, out int uv) && -uv >= CpuInfo.MinCPUUV && -uv <= CpuInfo.MaxCPUUV)
                        {
                            SmuStatus s = smu.SetCoPer(core, -uv);
                            Logger.WriteLine($"UV core {core}: {-uv} {s}");
                            if (s == SmuStatus.OK && uv != 0) _cpuUV = -uv;
                        }
                        core++;
                    }
                    lines.AppendLine($"CPU UV cores {cpuUVCores}");
                }
                else if (CpuInfo.IsSupportedUV() && cpuUV >= CpuInfo.MinCPUUV && cpuUV <= CpuInfo.MaxCPUUV)
                {
                    SmuStatus s = smu.SetCoAll(cpuUV);
                    Logger.WriteLine($"UV: {cpuUV} {s}");
                    if (s == SmuStatus.OK) _cpuUV = cpuUV;
                    lines.AppendLine($"CPU UV {cpuUV}: {s}");
                }

                if (CpuInfo.IsSupportedUViGPU() && igpuUV >= CpuInfo.MinIGPUUV && igpuUV <= CpuInfo.MaxIGPUUV)
                {
                    SmuStatus s = smu.SetCoGfx(igpuUV);
                    Logger.WriteLine($"iGPU UV: {igpuUV} {s}");
                    if (s == SmuStatus.OK) _igpuUV = igpuUV;
                    lines.AppendLine($"iGPU UV {igpuUV}: {s}");
                }

                SmuStatus? tempStatus = SetCPUTemp(cpuTemp, true);
                if (tempStatus.HasValue) lines.AppendLine($"CPU Temp {cpuTemp}°C: {tempStatus}");
            }
            catch (Exception ex)
            {
                Logger.WriteLine("UV Error: " + ex.ToString());
            }

            SetReapplyEnabled(AppConfig.IsApplyUV());
            return lines.ToString().TrimEnd();
        }

        public string ReadRyzenLimits()
        {
            var smu = GetSmu();
            if (smu == null) return string.Empty;

            try
            {
                PowerLimits? lim = smu.GetPowerLimits();
                if (lim == null) return string.Empty;

                string line = $"SPL: {lim.Stapm:F1}W | sPPT {lim.Slow:F1}W | fPPT {lim.Fast:F1}W";
                if (lim.ApuSlow.HasValue) line += $" | APU {lim.ApuSlow.Value:F1}W";
                line += $", Temp: {lim.TctlTemp:F0}°C";
                Logger.WriteLine("Ryzen Limits: " + line);
                return line;
            }
            catch (Exception ex)
            {
                Logger.WriteLine("ReadRyzenLimits Error: " + ex.ToString());
                return string.Empty;
            }
        }

        public void ResetRyzen()
        {
            if (_cpuUV != 0) SetUV(0);
            if (_igpuUV != 0) SetUViGPU(0);
            if (_cpuTemp != CpuInfo.DefaultTemp) SetCPUTemp(CpuInfo.DefaultTemp, true);
            SetReapplyEnabled(false);
        }

        public void AutoRyzen()
        {
            if (Modes.GetCurrent() == Modes.WindowsSlot) return;
            if (!CpuInfo.IsAMD) return;

            if (AppConfig.IsApplyUV()) SetRyzen();
            else ResetRyzen();
        }

        public void AutoCPUTemp()
        {
            if (Modes.GetCurrent() == Modes.WindowsSlot) return;
            if (!CpuInfo.IsAMD) return;
            if (!AppConfig.IsApplyUV()) return;
            if (!ProcessHelper.IsUserAdministrator()) return;

            try
            {
                SetCPUTemp(AppConfig.GetMode("cpu_temp"), true);
            }
            catch (Exception ex)
            {
                Logger.WriteLine("AutoCPUTemp Error: " + ex.Message);
            }
        }

        public void ShutdownReset()
        {
            if (!AppConfig.IsShutdownReset()) return;
            Program.acpi.DeviceSet(AsusACPI.PerformanceMode,AsusACPI.PerformanceBalanced, "Mode Reset");
        }

        public void SleepReset()
        {
            if (!AppConfig.IsSleepReset()) return;
            Program.acpi.DeviceSet(AsusACPI.PerformanceMode, Modes.GetCurrentBase(), "Sleep Reset");
        }

    }
}
