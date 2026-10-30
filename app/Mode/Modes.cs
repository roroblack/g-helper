namespace GHelper.Mode
{
    internal class Modes
    {
        static Dictionary<string, string> settings = new Dictionary<string, string>
        {
            { "mode_base", "_" },
            { "mode_name", "_" },
            { "powermode", "string" },
            { "limit_total", "int" },
            { "limit_slow", "int" },
            { "limit_fast", "int" },
            { "limit_cpu", "int" },
            { "limit_crossload", "int" },
            { "limit_gpucpu", "int" },
            { "limit_cputemp", "int" },
            { "fan_profile_cpu", "string" },
            { "fan_profile_gpu", "string" },
            { "fan_profile_mid", "string" }, 
            { "gpu_power", "int" },
            { "gpu_boost", "int" },
            { "gpu_temp", "int" },
            { "gpu_core", "int" },
            { "gpu_memory", "int" },
            { "gpu_clock_limit", "int" },
            { "cpu_temp", "_" },
            { "cpu_uv", "_" },
            { "cpu_uv_cores", "_" },
            { "igpu_uv", "_" },
            { "auto_boost", "int" },
            { "auto_apply", "int" },
            { "auto_apply_power", "int" },
            { "auto_uv", "_" },
            { "hysteresis_up", "int" },
            { "hysteresis_down", "int" }
        };

        const int maxModes = 20;

        // Built-in Manual mode (ProArt 4th operating mode). Slot 20 is outside the custom range 3..19.
        public const int ManualSlot = 20;
        public const int WindowsSlot = 21;
        public static bool IsCustom(int mode) => mode >= 3 && mode < maxModes;
        public static int GetCustomSlot()
        {
            int remembered = AppConfig.Get("last_custom_mode");
            if (IsCustom(remembered) && Exists(remembered)) return remembered;
            for (int i = 3; i < maxModes; i++) if (Exists(i)) return i;
            // Create a genuine, editable GHelper custom profile, not the firmware Manual mode.
            AppConfig.Set("mode_base_3", AsusACPI.PerformanceBalanced);
            AppConfig.Set("mode_name_3", "커스텀");
            return 3;
        }

        public static Dictionary<int, string> GetDictonary()
        {
            Dictionary<int, string> modes = new Dictionary<int, string>
            {
              {WindowsSlot, GetName(WindowsSlot)},
              {2, GetName(2)},
              {0, GetName(0)},
              {1, GetName(1)},
              {ManualSlot, GetName(ManualSlot)},
              {GetCustomSlot(), GetName(GetCustomSlot())}
            };

            for (int i = 3; i < maxModes; i++)
            {
                if (Exists(i) && !modes.ContainsKey(i)) modes.Add(i, GetName(i));
            }

            return modes;
        }

        public static List<int> GetList()
        {
            return new List<int> { WindowsSlot, 2, 0, 1, ManualSlot, GetCustomSlot() };
        }

        public static void Remove(int mode)
        {
            foreach (string clean in settings.Keys)
            {
                AppConfig.Remove(clean + "_" + mode);
            }
            // This association is deliberately not cloned by Add(): new custom modes default to unchanged.
            AppConfig.Remove("windows_power_mode_" + mode);
        }

        public static int Add()
        {
            int currentMode = GetCurrent();

            for (int i = 3; i < maxModes; i++)
            {
                if (Exists(i)) continue;

                AppConfig.Set("mode_base_" + i, GetCurrentBase());
                AppConfig.Set("mode_name_" + i, "Custom " + (i - 2));

                if (Exists(currentMode))
                {
                    foreach (var kvp in settings)
                    {
                        if (kvp.Value == "_") continue;

                        string sourceKey = kvp.Key + "_" + currentMode;
                        string targetKey = kvp.Key + "_" + i;

                        if (!AppConfig.Exists(sourceKey)) continue;

                        if (kvp.Value == "int")
                            AppConfig.Set(targetKey, AppConfig.Get(sourceKey));
                        else
                            AppConfig.Set(targetKey, AppConfig.GetString(sourceKey));
                    }
                }

                return i;
            }
            return -1;
        }

        public static void InitFullSpeed()
        {
            int vivoMode = Program.acpi.DeviceGet(AsusACPI.VivoBookMode);
            if (vivoMode < 0) return;
            Logger.WriteLine($"VivoBookMode: {vivoMode} (0x{vivoMode:X})");
            if ((vivoMode & 0x40000) == 0) return;

            for (int i = 3; i < maxModes; i++)
                if (GetBase(i) == AsusACPI.PerformanceFullSpeed) return;

            for (int i = 3; i < maxModes; i++)
            {
                if (Exists(i)) continue;
                AppConfig.Set("mode_base_" + i, AsusACPI.PerformanceFullSpeed);
                AppConfig.Set("mode_name_" + i, "Full Speed");
                return;
            }
        }

        public static int GetCurrent()
        {
            return AppConfig.Get("performance_mode");
        }

        public static bool IsCurrentCustom()
        {
            return IsCustom(GetCurrent());
        }

        public static void SetCurrent(int mode)
        {
            AppConfig.Set("performance_" + Program.PerformanceKey(), mode);
            AppConfig.Set("performance_mode", mode);
            if (IsCustom(mode)) AppConfig.Set("last_custom_mode", mode);
        }

        public static int GetCurrentBase()
        {
            return GetBase(GetCurrent());
        }

        public static string GetCurrentName()
        {
            return GetName(GetCurrent());
        }

        public static bool Exists(int mode)
        {
            return GetBase(mode) >= 0;
        }

        public static int GetBase(int mode)
        {
            if (mode == WindowsSlot) return AsusACPI.PerformanceBalanced;
            if (mode == ManualSlot)
                return AsusACPI.PerformanceManual;
            if (mode >= 0 && mode <= 2)
                return mode;
            else
                return AppConfig.Get("mode_base_" + mode);
        }

        public static string GetName(int mode)
        {
            switch (mode)
            {
                case WindowsSlot: return "Windows(윈도우)";
                case ManualSlot:
                    return "수동";
                case 0:
                    return "표준";
                case 1:
                    return "성능";
                case 2:
                    return "저소음";
                default:
                    return AppConfig.GetString("mode_name_" + mode);
            }
        }


        public static int GetNext(bool back = false)
        {
            var modes = GetList();
            int index = modes.IndexOf(GetCurrent());

            if (back)
            {
                index--;
                if (index < 0) index = modes.Count - 1;
                return modes[index];
            }
            else
            {
                index++;
                if (index > modes.Count - 1) index = 0;
                return modes[index];
            }
        }
    }
}
