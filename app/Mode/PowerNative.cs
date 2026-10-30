using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace GHelper.Mode
{
    internal class PowerNative
    {
        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerWriteDCValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid,
            int AcValueIndex);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerWriteACValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid,
            int AcValueIndex);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerReadACValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid,
            out IntPtr AcValueIndex
            );

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerReadDCValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingsGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid,
            out IntPtr AcValueIndex
            );


        [DllImport("powrprof.dll")]
        static extern uint PowerReadACValue(
            IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupOfPowerSettingGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid PowerSettingGuid,
            ref int Type,
            ref IntPtr Buffer,
            ref uint BufferSize
            );


        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerSetActiveScheme(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid);

        [DllImport("PowrProf.dll", CharSet = CharSet.Unicode)]
        static extern UInt32 PowerGetActiveScheme(IntPtr UserPowerKey, out IntPtr ActivePolicyGuid);

        static readonly Guid GUID_CPU = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        static readonly Guid GUID_BOOST = new Guid("be337238-0d82-4146-a960-4f3749d470c7");

        private static Guid GUID_SLEEP_SUBGROUP = new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static Guid GUID_HIBERNATEIDLE = new Guid("9d7815a6-7ee4-497e-8888-515a05f02364");

        private static Guid GUID_SYSTEM_BUTTON_SUBGROUP = new Guid("4f971e89-eebd-4455-a8de-9e59040e7347");
        private static Guid GUID_LIDACTION = new Guid("5CA83367-6E45-459F-A27B-476B1D01C936");

        private static Guid GUID_SUB_PCIEXPRESS = new Guid("501a4d13-42af-4429-9fd1-a8218c268e20");
        private static Guid GUID_PCI_EXPRESS_ASPM = new Guid("ee12f906-d277-404b-b6da-e5fa1a576df5");

        private static Guid GUID_SUB_NONE = new Guid("fea3413e-7e05-4911-9a71-700331f1c294");
        private static Guid GUID_CONNECTIVITY_IN_STANDBY = new Guid("f15576e8-98b7-4186-b944-eafa664402d9");

        [DllImportAttribute("powrprof.dll", EntryPoint = "PowerGetActualOverlayScheme")]
        public static extern uint PowerGetActualOverlayScheme(out Guid ActualOverlayGuid);

        [DllImportAttribute("powrprof.dll", EntryPoint = "PowerGetEffectiveOverlayScheme")]
        public static extern uint PowerGetEffectiveOverlayScheme(out Guid EffectiveOverlayGuid);

        [DllImportAttribute("powrprof.dll", EntryPoint = "PowerSetActiveOverlayScheme")]
        public static extern uint PowerSetActiveOverlayScheme([MarshalAs(UnmanagedType.LPStruct)] Guid OverlaySchemeGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        // Overlay getters return a GUID into caller-owned storage (not a GUID** allocation).
        // Only PowerGetActiveScheme allocates memory which the caller frees with LocalFree.
        public static string ApplyOverlayOnly(Guid overlay)
        {
            try
            {
                return PerModePower.ApplyVerified(overlay, GetBatterySaverStatus,
                    () => { uint error = PowerGetActualOverlayScheme(out Guid actual); return (error, actual); },
                    () => { uint error = PowerGetEffectiveOverlayScheme(out Guid effective); return (error, effective); },
                    PowerSetActiveOverlayScheme);
            }
            catch (EntryPointNotFoundException) { return "unsupported-api"; }
            catch (DllNotFoundException) { return "unsupported-api"; }
        }

        // Diagnostic observations only: neither getter is promoted to unique OS truth.
        // Read independently so a missing/failed actual export cannot hide effective.
        public static string ReadOverlayAudit()
        {
            static string Read(string name, Func<(uint Status, Guid Value)> read)
            {
                try { var v = read(); return $"{name}Status={v.Status} {name}={v.Value:D}"; }
                catch (Exception ex) { return $"{name}Status=exception:{ex.GetType().Name} {name}=unavailable"; }
            }
            return Read("actual", () => { uint s = PowerGetActualOverlayScheme(out Guid g); return (s, g); }) + " " +
                Read("effective", () => { uint s = PowerGetEffectiveOverlayScheme(out Guid g); return (s, g); });
        }

        const string POWER_SILENT = "961cc777-2547-4f9d-8174-7d86181b8a7a";
        const string POWER_BALANCED = "00000000-0000-0000-0000-000000000000";
        const string POWER_TURBO = "ded574b5-45a0-4f42-8737-46345c09c238";

        const string PLAN_BALANCED = "381b4222-f694-41f0-9685-ff5bb260df2e";
        const string PLAN_HIGH_PERFORMANCE = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        static List<string> overlays = new() {
                POWER_BALANCED,
                POWER_TURBO,
                POWER_SILENT,
            };

        public static Dictionary<string, string> powerModes = new Dictionary<string, string>
            {
                { POWER_SILENT, "Best Power Efficiency" },
                { POWER_BALANCED, "Balanced" },
                { POWER_TURBO, "Best Performance" },
                { PLAN_HIGH_PERFORMANCE, "High Performance Plan"},
            };
        static Guid GetActiveScheme()
        {
            uint hr = PowerGetActiveScheme(IntPtr.Zero, out IntPtr pointer);
            if (hr != 0 || pointer == IntPtr.Zero)
                throw new InvalidOperationException($"PowerGetActiveScheme failed: {hr}");
            try { return Marshal.PtrToStructure<Guid>(pointer); }
            finally { LocalFree(pointer); }
        }

        public static int GetCPUBoost()
        {
            IntPtr AcValueIndex;
            Guid activeSchemeGuid = GetActiveScheme();

            UInt32 value = PowerReadACValueIndex(IntPtr.Zero,
                 activeSchemeGuid,
                 GUID_CPU,
                 GUID_BOOST, out AcValueIndex);

            return AcValueIndex.ToInt32();

        }

        public static void SetCPUBoost(int boost = 0)
        {
            Guid activeSchemeGuid = GetActiveScheme();

            if (boost == GetCPUBoost()) return;

            var hrAC = PowerWriteACValueIndex(
                 IntPtr.Zero,
                 activeSchemeGuid,
                 GUID_CPU,
                 GUID_BOOST,
                 boost);

            PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid);

            var hrDC = PowerWriteDCValueIndex(
                 IntPtr.Zero,
                 activeSchemeGuid,
                 GUID_CPU,
                 GUID_BOOST,
                 boost);

            PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid);

            Logger.WriteLine("Boost " + boost);
        }

        public static string GetPowerMode()
        {
            if (GetActiveScheme().ToString() == PLAN_HIGH_PERFORMANCE) return PLAN_HIGH_PERFORMANCE;
            PowerGetEffectiveOverlayScheme(out Guid activeScheme);
            return activeScheme.ToString();
        }

        public static string GetOSOverlay()
        {
            uint status = PowerGetEffectiveOverlayScheme(out Guid overlay);
            if (status != 0) throw new InvalidOperationException($"Windows overlay capture failed: {status}");
            if (!overlays.Contains(overlay.ToString())) throw new InvalidOperationException("Windows overlay unsupported; snapshot unchanged");
            return overlay.ToString();
        }

        public static string GetPowerPlan() => GetActiveScheme().ToString();

        public static int GetOSBoost(bool dc)
        {
            Guid scheme = GetActiveScheme();
            IntPtr value;
            uint status = dc ? PowerReadDCValueIndex(IntPtr.Zero, scheme, GUID_CPU, GUID_BOOST, out value)
                : PowerReadACValueIndex(IntPtr.Zero, scheme, GUID_CPU, GUID_BOOST, out value);
            return status == 0 ? value.ToInt32() : -1;
        }

        public static PolicyRestoreResult RestoreOSPolicy(string plan, string overlay, int boostAC, int boostDC)
        {
            try
            {
                // Validate the complete saved snapshot before performing any OS writes. No guessed defaults.
                if (!Guid.TryParse(plan, out var originalPlan) || originalPlan == Guid.Empty ||
                    !Guid.TryParse(overlay, out var originalOverlay) || !overlays.Contains(overlay) ||
                    boostAC < 0 || boostAC > 6 || boostDC < 0 || boostDC > 6)
                    return new(false, "Windows snapshot invalid or incomplete; no OS writes attempted");
                uint planStatus = PowerSetActiveScheme(IntPtr.Zero, originalPlan);
                if (planStatus != 0) throw new InvalidOperationException($"Windows plan restore failed: {planStatus}");
                if (!string.Equals(GetPowerPlan(), plan, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Windows plan restore readback mismatch");
                if (PowerWriteACValueIndex(IntPtr.Zero, originalPlan, GUID_CPU, GUID_BOOST, boostAC) != 0)
                    throw new InvalidOperationException("Windows AC boost restore failed");
                if (PowerWriteDCValueIndex(IntPtr.Zero, originalPlan, GUID_CPU, GUID_BOOST, boostDC) != 0)
                    throw new InvalidOperationException("Windows DC boost restore failed");
                if (PowerSetActiveScheme(IntPtr.Zero, originalPlan) != 0)
                    throw new InvalidOperationException("Windows plan activation failed");
                if (GetOSBoost(false) != boostAC || GetOSBoost(true) != boostDC)
                    throw new InvalidOperationException("Windows AC/DC boost readback mismatch");
                string overlayResult = ApplyOverlayOnly(originalOverlay);
                if (overlayResult != "applied") throw new InvalidOperationException("Windows overlay restore: " + overlayResult);
                return new(true, "Windows plan, AC/DC boost and effective overlay verified; firmware effective mode unverified");
            }
            catch (Exception ex) { return new(false, ex.GetType().Name + ": " + ex.Message); }
        }

        public static void SetPowerMode(string scheme)
        {

            if (scheme == PLAN_HIGH_PERFORMANCE)
            {
                SetPowerPlan(scheme);
                return;
            }
            else
            {
                // Power plan from config or defaulting to balanced
                string plan = AppConfig.GetModeString("scheme");
                if (Program.currentSource == Program.PowerSource.USBC && AppConfig.GetModeString("scheme_usbc") is string usbc) plan = usbc;
                SetPowerPlan(plan);            
            }

            if (!overlays.Contains(scheme)) return;

            Guid guidScheme = new Guid(scheme);

            uint status = PowerGetEffectiveOverlayScheme(out Guid activeScheme);

            if (GetBatterySaverStatus())
            {
                Logger.WriteLine("Battery Saver detected");
                return;
            }

            if (status != 0 || activeScheme != guidScheme)
            {
                status = PowerSetActiveOverlayScheme(guidScheme);
                Logger.WriteLine("Power Mode " + activeScheme + " -> " + scheme + ":" + (status == 0 ? "OK" : status));
            }

        }

        public static void SetPowerPlan(string scheme)
        {
            // Skipping power modes
            if (overlays.Contains(scheme)) return;

            if (scheme is null) scheme = PLAN_BALANCED;
            var activeScheme = GetActiveScheme().ToString();
            if (activeScheme == scheme) return;

            uint status = PowerSetActiveScheme(IntPtr.Zero, new Guid(scheme));
            Logger.WriteLine($"Power Plan {activeScheme} -> {scheme} :" + (status == 0 ? "OK" : status));
        }

        public static string GetDefaultPowerMode(int mode)
        {
            switch (mode)
            {
                case 1: // turbo
                    return POWER_TURBO;
                case 2: //silent
                    return POWER_SILENT;
                case 3:
                    return PLAN_HIGH_PERFORMANCE;
                default: // balanced
                    return POWER_BALANCED;
            }
        }

        public static void SetPowerMode(int mode)
        {
            SetPowerMode(GetDefaultPowerMode(mode));
        }

        public static int GetASPM()
        {
            Guid activeSchemeGuid = GetActiveScheme();
            IntPtr activeIndex;

            PowerReadACValueIndex(IntPtr.Zero,
                    activeSchemeGuid,
                    GUID_SUB_PCIEXPRESS,
                    GUID_PCI_EXPRESS_ASPM, out activeIndex);

            return activeIndex.ToInt32();
        }

        public static void SetASPM(int status = 0)
        {
            Guid activeSchemeGuid = GetActiveScheme();
            var currentASPM = GetASPM();
            if (currentASPM == status) return;

            var hrAC = PowerWriteACValueIndex(
                IntPtr.Zero,
                activeSchemeGuid,
                GUID_SUB_PCIEXPRESS,
                GUID_PCI_EXPRESS_ASPM,
                status);

            PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid);
            Logger.WriteLine($"Changed AC ASPM {currentASPM} -> {status}");
        }

        public static void SetBalancedASPM(int status = 0)
        {
            if (GetActiveScheme().ToString() != PLAN_BALANCED) return;
            SetASPM(status);
        }

        public static void SetConnectivityInStandby(int ac = 0, int dc = 0)
        {
            Guid activeSchemeGuid = GetActiveScheme();

            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes\{activeSchemeGuid}\{GUID_CONNECTIVITY_IN_STANDBY}");
            if (key != null && (int?)key.GetValue("ACSettingIndex") == ac && (int?)key.GetValue("DCSettingIndex") == dc) return;

            var hrAC = PowerWriteACValueIndex(
                IntPtr.Zero,
                activeSchemeGuid,
                GUID_SUB_NONE,
                GUID_CONNECTIVITY_IN_STANDBY,
                ac);

            var hrDC = PowerWriteDCValueIndex(
                IntPtr.Zero,
                activeSchemeGuid,
                GUID_SUB_NONE,
                GUID_CONNECTIVITY_IN_STANDBY,
                dc);

            PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid);
            Logger.WriteLine($"Connectivity in Standby {ac}/{dc}: " + (hrAC == 0 && hrDC == 0 ? "OK" : $"{hrAC}/{hrDC}"));
        }

        public static int GetLidAction(bool ac)
        {
            Guid activeSchemeGuid = GetActiveScheme();

            IntPtr activeIndex;
            if (ac)
                PowerReadACValueIndex(IntPtr.Zero,
                     activeSchemeGuid,
                     GUID_SYSTEM_BUTTON_SUBGROUP,
                     GUID_LIDACTION, out activeIndex);

            else
                PowerReadDCValueIndex(IntPtr.Zero,
                    activeSchemeGuid,
                    GUID_SYSTEM_BUTTON_SUBGROUP,
                    GUID_LIDACTION, out activeIndex);


            return activeIndex.ToInt32();
        }


        public static void SetLidAction(int action, bool acOnly = false)
        {
            /**
             * 1: Do nothing
             * 2: Seelp
             * 3: Hibernate
             * 4: Shutdown
             */

            Guid activeSchemeGuid = GetActiveScheme();

            var hrAC = PowerWriteACValueIndex(
                IntPtr.Zero,
                activeSchemeGuid,
                GUID_SYSTEM_BUTTON_SUBGROUP,
                GUID_LIDACTION,
                action);

            PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid);

            if (!acOnly)
            {
                var hrDC = PowerWriteDCValueIndex(
                  IntPtr.Zero,
                  activeSchemeGuid,
                  GUID_SYSTEM_BUTTON_SUBGROUP,
                  GUID_LIDACTION,
                  action);

                PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid);
            }

            Logger.WriteLine("Changed Lid Action to " + action);
        }

        public static int GetHibernateAfter()
        {
            Guid activeSchemeGuid = GetActiveScheme();
            IntPtr seconds;
            PowerReadDCValueIndex(IntPtr.Zero,
                    activeSchemeGuid,
                    GUID_SLEEP_SUBGROUP,
                    GUID_HIBERNATEIDLE, out seconds);

            Logger.WriteLine("Hibernate after " + seconds);
            return (seconds.ToInt32() / 60);
        }


        public static void SetHibernateAfter(int minutes)
        {
            int seconds = minutes * 60;

            Guid activeSchemeGuid = GetActiveScheme();
            var hrAC = PowerWriteDCValueIndex(
                IntPtr.Zero,
                activeSchemeGuid,
                GUID_SLEEP_SUBGROUP,
                GUID_HIBERNATEIDLE,
                seconds);

            PowerSetActiveScheme(IntPtr.Zero, activeSchemeGuid);

            Logger.WriteLine("Setting Hibernate after " + seconds + ": " + (hrAC == 0 ? "OK" : hrAC));
        }

        [DllImport("Kernel32")]
        private static extern bool GetSystemPowerStatus(SystemPowerStatus sps);
        public enum ACLineStatus : byte
        {
            Offline = 0, Online = 1, Unknown = 255
        }

        public enum BatteryFlag : byte
        {
            High = 1,
            Low = 2,
            Critical = 4,
            Charging = 8,
            NoSystemBattery = 128,
            Unknown = 255
        }

        // Fields must mirror their unmanaged counterparts, in order
        [StructLayout(LayoutKind.Sequential)]
        public class SystemPowerStatus
        {
            public ACLineStatus ACLineStatus;
            public BatteryFlag BatteryFlag;
            public Byte BatteryLifePercent;
            public Byte SystemStatusFlag;
            public Int32 BatteryLifeTime;
            public Int32 BatteryFullLifeTime;
        }

        [DllImport("ntdll.dll")]
        static extern int RtlPublishWnfStateData(ulong StateName, IntPtr TypeId, byte[] Buffer, int Length, IntPtr ExplicitScope);

        public static void SetEnergySaver(bool status)
        {
            int result = RtlPublishWnfStateData(0x41C6013DA3BC3075, IntPtr.Zero, BitConverter.GetBytes(status ? 1 : 2), 4, IntPtr.Zero);
            Logger.WriteLine($"Energy Saver {status}: {result}");
        }

        public static bool GetBatterySaverStatus()
        {
            try
            {
                var status = Registry.GetValue(@"HKEY_LOCAL_MACHINE\System\CurrentControlSet\Control\Power", "EnergySaverState", null);
                if (status == null)
                {
                    SystemPowerStatus sps = new SystemPowerStatus();
                    GetSystemPowerStatus(sps);
                    return (sps.SystemStatusFlag > 0);
                }
                return (int)status == 1;
            }
            catch (Exception e)
            {
                Logger.WriteLine("Can't check EnergySaverState" + e.Message);
                return false;
            }
        }


    }
}
