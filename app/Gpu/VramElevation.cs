using GHelper.Helpers;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace GHelper;

// A one-shot, allowlisted action. No file, variable name, GUID or command can be
// supplied by the caller. This path runs before Program initializes the app.
internal static class VramElevation
{
    internal const string Action = "--vram-uefi-apply";
    internal static bool Korean => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";
    internal static string SelectionHint => Korean
        ? "펌웨어 설정값이며 현재 GPU 할당량이 아닙니다. 값을 선택하면 저장을 요청합니다. UEFI 저장 시 관리자 UAC 승인이 필요합니다. MyASUS는 별도의 권한 있는 ASUS 서비스를 사용하지만 G-Helper는 해당 보안 검사를 우회하거나 서비스를 자동 시작하지 않습니다. 저장 후 검증에 성공한 경우에만 재시작을 제안합니다."
        : "Firmware selection, not active GPU allocation. Selecting a value requests a save. UEFI saves require administrator UAC approval. MyASUS uses a separate privileged ASUS service; G-Helper does not bypass its authorization or start it automatically. Restart is offered only after verified storage.";
    internal static string ErrorTitle => Korean ? "GPU 메모리 저장 실패" : "GPU memory save failed";
    internal static string PendingTitle => Korean ? "GPU 메모리 설정 저장됨 · 재시작 대기" : "GPU memory setting saved — restart pending";
    internal static string PendingText => Korean
        ? "UEFI 변수 저장 후 다시 읽어 요청값과 속성을 확인했습니다. 현재 GPU 할당량이 변경되었다는 뜻은 아닙니다.\n\n재시작 후 펌웨어 현재값과 GPU 드라이버 할당량을 확인해야 합니다. 지금 다시 시작하시겠습니까?"
        : "The UEFI variable payload and attributes were read back and verified. This does not verify the active GPU allocation.\n\nAfter reboot, check the firmware current value and GPU-driver allocation. Restart now?";

    internal static bool IsHelperAction(string[] args) => args.Length > 0 && args[0].StartsWith("--vram-uefi", StringComparison.Ordinal);

    internal static bool TryParse(string[] args, out int raw)
    {
        raw = -1;
        if (args.Length != 2 || args[0] != Action ||
            !ushort.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out ushort value) ||
            args[1] != value.ToString(CultureInfo.InvariantCulture)) return false;
        raw = value;
        return true;
    }

    internal static ProcessStartInfo CreateStartInfo(string executable, int raw)
    {
        if (raw < 0 || raw > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(raw));
        // FileName is a separate ShellExecute field (never quote it as an argument).
        // The sole argument is canonical unsigned decimal, so no user text is quoted.
        return new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = Action + " " + raw.ToString(CultureInfo.InvariantCulture)
        };
    }

    // Replace only in the hardware-free harness; production always awaits the
    // exact helper process, which never restarts/exits the original tray app.
    internal static Func<ProcessStartInfo, Task<int>> Launch { get; set; } = async info =>
    {
        using Process process = Process.Start(info) ?? throw new InvalidOperationException("No helper process was created.");
        await process.WaitForExitAsync();
        return process.ExitCode;
    };

    internal static async Task<(bool Success, string Error)> ApplyAsync(int raw)
    {
        try
        {
            Logger.WriteLine($"VRAM elevation requested raw={raw}; explicit user selection");
            int code = await Launch(CreateStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable."), raw));
            Logger.WriteLine($"VRAM elevation completed raw={raw} exit={code}");
            // Helper already displays the detailed firmware/privilege error.
            return (code == 0, "");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Logger.WriteLine("VRAM elevation cancelled: Win32 1223; no helper write performed");
            return (false, Korean ? "관리자 권한 요청을 취소했습니다(Win32 1223). 메모리 설정을 저장하지 않았습니다." : "Administrator request cancelled (Win32 1223). No memory setting was saved.");
        }
        catch (Exception ex)
        {
            Logger.WriteLine("VRAM elevation failed: " + ex);
            return (false, (Korean ? "관리자 메모리 저장 도우미를 실행하지 못했습니다: " : "Could not run the elevated memory helper: ") + ex.Message);
        }
    }

    internal static int ExecuteHelper(string[] args)
    {
        if (!TryParse(args, out int raw)) return 87;
        try
        {
            if (!ProcessHelper.IsUserAdministrator())
            {
                ShowError(Korean ? "관리자 승격이 확인되지 않았습니다(Win32 740). 저장하지 않았습니다." : "The helper is not elevated (Win32 740). No write attempted.");
                return 740;
            }
            Logger.WriteLine($"VRAM helper begin raw={raw}; elevated=True; no app startup actions");
            var acpi = new AsusACPI(firmwareHelperOnly: true);
            try
            {
                string error = "ACPI firmware table unavailable. No write attempted.";
                if (!acpi.IsConnected() || !acpi.SetUefiVramMem(raw, out error))
                {
                    ShowError(error);
                    return 1;
                }
                Logger.WriteLine($"VRAM helper verified raw={raw}; reboot pending; active allocation unverified");
                return 0;
            }
            finally { acpi.Close(); }
        }
        catch (Exception ex)
        {
            Logger.WriteLine("VRAM helper exception: " + ex);
            ShowError(ex.Message);
            return 1;
        }
    }

    private static void ShowError(string error)
    {
        Logger.WriteLine("VRAM helper failed: " + error);
        MessageBox.Show(error + (Korean
            ? "\n\n현재 GPU 할당량은 확인되지 않았습니다. 설정 기록 및 재시작 요청을 하지 않습니다. 저장 후 검증 실패라면 변수가 이미 기록되었을 수 있습니다."
            : "\n\nActive GPU allocation is unverified. No configuration record or reboot request will be made. A post-write verification failure may mean the variable was already stored."),
            ErrorTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
