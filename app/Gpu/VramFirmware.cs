namespace GHelper;

internal interface IFirmwareVariables
{
    bool Write(string name, string guid, byte[]? data, uint attributes, out int error);
    bool Read(string name, string guid, out byte[] data, out uint attributes, out int error);
}

internal static class VramFirmware
{
    internal const string Name = "AS000600C4";
    internal const string Guid = "{607005d5-3f75-4b2e-98f0-85ba66797a3e}";
    internal const uint Attributes = 7;
    internal static IFirmwareVariables Variables { get; set; } = new WindowsFirmwareVariables();
    public static bool TrySet(int value, out string error)
    {
        error = "";
        byte[]? payload = value == 0 ? null : BitConverter.GetBytes((uint)value);
        if (!Variables.Write(Name, Guid, payload, Attributes, out int code) && !(value == 0 && code == 203))
        {
            string stage = Variables is WindowsFirmwareVariables native ? native.LastFailureStage : "UEFI write";
            bool ko = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";
            string detail = code switch
            {
                1300 => ko ? "현재 토큰에 SeSystemEnvironmentPrivilege가 없습니다. 관리자 승격 후에도 이 오류가 나면 로컬 보안 정책의 '펌웨어 환경 값 수정' 권한을 관리자에게 확인하세요. 권한 활성화는 토큰에 없는 권한을 추가하지 못합니다."
                    : "The token does not contain SeSystemEnvironmentPrivilege. Enabling cannot add a missing privilege. If this persists after UAC elevation, ask the administrator to check the Modify firmware environment values policy.",
                1314 => ko ? "SeSystemEnvironmentPrivilege 권한이 활성화되지 않았거나 펌웨어 API가 권한을 거부했습니다."
                    : "SeSystemEnvironmentPrivilege is not held or the firmware API rejected the privilege.",
                5 => ko ? "Windows 또는 펌웨어가 접근을 거부했습니다. 관리자 실행만으로 해결되지 않을 수 있습니다."
                    : "Windows or firmware denied access; administrator access alone may not resolve this.",
                _ => new System.ComponentModel.Win32Exception(code).Message
            };
            error = $"{stage} failed (Win32 {code}). {detail}";
            Logger.WriteLine($"VRAM failure stage={stage} Win32={code}; active allocation unverified");
            return false;
        }
        bool read = Variables.Read(Name, Guid, out byte[] actual, out uint attributes, out code);
        if (value == 0 && !read && code == 203) return true;
        if (!read)
        {
            error = $"UEFI readback failed (Win32 {code}). The request may be stored, but is unverified.";
            return false;
        }
        if (payload is null || attributes != Attributes || !actual.AsSpan().SequenceEqual(payload))
        {
            error = "UEFI readback mismatch. The request may be stored, but is unverified.";
            return false;
        }
        return true;
    }
}
