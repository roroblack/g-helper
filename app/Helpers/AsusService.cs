using GHelper.Ally;
using System.Diagnostics;
using System.Management;

namespace GHelper.Helpers
{
    public static class AsusService
    {

        static List<string> services = new() {
                "ArmouryCrateControlInterface",
                "ArmouryCrateProArtService",
                "AsHidService",
                "ASUSOptimization",
                "AsusAppService",
                "ASUSLinkNear",
                "ASUSLinkRemote",
                "ASUSSoftwareManager",
                "ASUSLiveUpdateAgent",
                "ASUSSwitch",
                "ASUSSystemAnalysis",
                "ASUSSystemDiagnosis",
                "ASUSXGMobileService",
                "AsusCertService",
                "ASUSProArtService",
                "ASUSProArtUpdateService",
        };

        // ProArt Creator Hub processes (not services) stopped together with the services above
        static List<string> proArtProcesses = new() {
                "AsusProArtHost",
                "AsusProArtService",
                "AsusProArtBackgroundTaskUWP",
        };

        // Items shown as on/off options in the Extra window. "ProArtProcesses" is a pseudo-entry.
        public static List<string> OptionalNames => (AppConfig.IsStopAC() ? services.Concat(servicesAC) : services).Concat(new[] { "ProArtProcesses" }).ToList();

        // Default is on (stop). Stored as AppConfig key "svc_<name>".
        public static bool IsEnabled(string name) => AppConfig.Get("svc_" + name, 1) == 1;

        // Current state for display in the Extra window: 실행 중 / 중지됨 / 없음
        public static string StateText(string name)
        {
            if (name == "ProArtProcesses")
                return proArtProcesses.Any(IsRunning) ? "실행 중" : "중지됨";
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT State FROM Win32_Service WHERE Name='{name}'");
                foreach (ManagementObject mo in searcher.Get())
                {
                    string state = (string)mo["State"];
                    return state switch
                    {
                        "Running" => "실행 중",
                        "Stopped" => "중지됨",
                        _ => state,
                    };
                }
                return "없음";
            }
            catch
            {
                return "없음";
            }
        }

        //"AsusPTPService",

        static List<string> servicesAC = new() {
                "ArmouryCrateSEService",
                "ArmouryCrateService",
                "LightingService",
        };

        private static bool IsRunning(string name)
        {
            var procs = Process.GetProcessesByName(name);
            try { return procs.Length > 0; }
            finally { foreach (var p in procs) p.Dispose(); }
        }

        public static bool IsAsusOptimizationRunning() => IsRunning("AsusOptimization");

        public static bool IsArmouryRunning()
        {
            var acService = IsRunning("ArmouryCrate.Service");
            var lightingService = IsRunning("LightingService");
            Logger.WriteLine($"AC Service: {acService}, Lighting Service: {lightingService}");
            return acService || lightingService;
        }

        public static void RunArmouryUninstaller()
        {
            Process.Start(new ProcessStartInfo("https://dlcdnets.asus.com/pub/ASUS/mb/14Utilities/Armoury_Crate_Uninstall_Tool.zip") { UseShellExecute = true });
        }

        public static bool IsOSDRunning() => IsRunning("AsusOSD");


        private static Dictionary<string, string> GetServiceStates(IEnumerable<string>? selected = null)
        {
            var names = ServiceActionPlan.Select(selected ?? OptionalNames.Where(IsEnabled), OptionalNames)
                .Where(n => n != "ProArtProcesses").ToList();
            var states = new Dictionary<string, string>();
            if (names.Count == 0) return states; // Never issue an empty WHERE query.
            try
            {
                string filter = string.Join(" OR ", names.Select(name => $"Name='{name}'"));
                using var searcher = new ManagementObjectSearcher($"SELECT Name, State FROM Win32_Service WHERE {filter}");
                foreach (ManagementObject mo in searcher.Get())
                    states[(string)mo["Name"]] = (string)mo["State"];
            }
            catch (Exception ex)
            {
                Logger.WriteLine(ex.Message);
            }
            return states;
        }

        private static List<string> GetRunningServices()
        {
            return GetServiceStates().Where(s => s.Value != "Stopped").Select(s => s.Key).ToList();
        }

        public static int GetRunningCount()
        {
            return GetRunningServices().Count;
        }


        public static void StopAsusServices()
        {
            ApplyAction(false, OptionalNames.Where(IsEnabled));
        }

        private static void StopProArtProcesses()
        {
                foreach (string proc in proArtProcesses)
                {
                    foreach (var p in Process.GetProcessesByName(proc))
                    {
                        try { p.Kill(); } catch (Exception ex) { Logger.WriteLine($"ProArt kill {proc}: {ex.Message}"); }
                        finally { p.Dispose(); }
                    }
                }
        }

        public static void StartAsusServices()
        {
            ApplyAction(true, OptionalNames.Where(IsEnabled));
        }

        public static void ApplyAction(bool start, IEnumerable<string> requested)
        {
            var selected = ServiceActionPlan.Select(requested, OptionalNames);
            if (selected.Count == 0) { Logger.WriteLine("Service action: no targets selected; no-op"); return; }
            ServiceActionPlan.Execute(start, selected, GetServiceStates(selected),
                name => ProcessHelper.StartEnableService(name),
                name => ProcessHelper.StopDisableService(name, servicesAC.Contains(name) ? "Manual" : "Disabled"),
                StopProArtProcesses);
            if (start) AppConfig.Set("services_disabled", 0);
            else
            {
                if (GetRunningCount() == 0) AppConfig.Set("services_disabled", 1);
                if (AppConfig.IsAlly()) AllyControl.ApplyMode((ControllerMode)AppConfig.Get("controller_mode", (int)ControllerMode.Auto), true);
            }
        }

        public static void StopOnStartup()
        {
            if (AppConfig.Is("services_skip")) return;
            if (!AppConfig.Is("services_disabled") || !ProcessHelper.IsUserAdministrator()) return;
            if (GetRunningCount() == 0) return;

            Logger.WriteLine("ASUS services revived, re-stopping on startup");
            Task.Run(() =>
            {
                StopAsusServices();
                Program.inputDispatcher?.Init();
            });
        }

    }

}