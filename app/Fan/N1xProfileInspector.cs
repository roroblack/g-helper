using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GHelper.Fan;

// Raw firmware evidence is deliberately NOT an editable/applicable fan curve.
// Vendor UI RelocatePostion clamps temperatures to 30+10*i..39+10*i and bytes
// >100 to 100. Never label that lossy transformation as a measured BIOS curve.
internal static class N1xProfileInspector
{
    internal static byte[] CreateReadRequest(uint id, int? selector)
    {
        bool curve = id is 0x110024 or 0x110025;
        bool scalar = id is 0x110026 or 0x110027 or 0x110034 or 0x1200A0 or 0x1200A3;
        if (!(curve && selector is >= 0 and <= 2) && !(scalar && selector is null))
            throw new ArgumentException("Only vendor-traced read endpoints/selectors are allowed.");
        byte[] request = new byte[curve ? 16 : 12];
        BitConverter.GetBytes(0x53545344u).CopyTo(request, 0); // DSTS; never DEVS
        BitConverter.GetBytes(curve ? 8u : 4u).CopyTo(request, 4);
        BitConverter.GetBytes(id).CopyTo(request, 8);
        if (curve) BitConverter.GetBytes(selector!.Value).CopyTo(request, 12);
        return request;
    }

    // Explicit user-requested diagnostic only. No static constructor, timer,
    // app startup read, service DLL, INIT, or any setter exists in this class.
    internal static string Sample()
    {
        var text = new StringBuilder($"{DateTimeOffset.Now:O} / {RuntimeInformation.ProcessArchitecture}\r\n");
        text.AppendLine("제조사 DSTS 원본 읽기 전용. selector는 펌웨어 테이블 인덱스이며 UI 모드 번호가 아닙니다.");
        text.AppendLine("수동/커스텀 쓰기 미검증. 읽기 성공은 적용 성공이 아닙니다.\r\n");
        using var handle = CreateFileW(@"\\.\ATKACPI", 0xC0000000, 3, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
        if (handle.IsInvalid) return text + $"N/A: ATKACPI open Win32={Marshal.GetLastWin32Error()}";
        void Read(uint id, int? selector, string label)
        {
            byte[] input = CreateReadRequest(id, selector), output = new byte[1024];
            bool ok = DeviceIoControl(handle, 0x22240c, input, (uint)input.Length, output, 1024, out uint returned, IntPtr.Zero);
            int error = ok ? 0 : Marshal.GetLastWin32Error();
            text.AppendLine($"{label} ID=0x{id:X8} selector={selector?.ToString() ?? "없음"} ok={ok} returned={returned} error={error}");
            if (!ok || returned < 4) { text.AppendLine("N/A\r\n"); return; }
            text.AppendLine("raw(first16)=" + Convert.ToHexString(output.AsSpan(0, (int)Math.Min(returned, 16))));
            if (id is 0x110024 or 0x110025) text.Append(DecodeCurve(ok,returned,output));
            else if (id is 0x110026 or 0x110027)
                text.AppendLine(returned < 8 ? "N/A: 짧은 lower-limit 응답" : "팬 lower-limit 원본 8바이트: " + string.Join(", ",output.Take(8)));
            else if (id == 0x110034)
                text.AppendLine(returned < 8 ? "N/A: 짧은 hysteresis 응답" : $"CPU up={output[0]} down={output[1]} support={output[2]}; GPU up={output[4]} down={output[5]} support={output[6]}; 레벨 의미/안전 범위 미검증");
            else text.AppendLine($"지원 bit16={(BitConverter.ToUInt32(output,0) & 0x10000) != 0}; data16={BitConverter.ToUInt16(output,0)}. 현재 PL1/PL2 W로 해석하지 않음.");
            text.AppendLine();
        }
        foreach (uint id in new uint[]{0x110024,0x110025})
            for (int selector=0;selector<3;selector++) Read(id,selector,id==0x110024?"CPU 커브":"GPU 커브");
        Read(0x110026,null,"CPU lower-limit"); Read(0x110027,null,"GPU lower-limit");
        Read(0x110034,null,"팬 히스테리시스");
        Read(0x1200A0,null,"CPU 전력 endpoint A0"); Read(0x1200A3,null,"CPU 전력 endpoint A3");
        text.AppendLine("주의: returned는 드라이버 출력 버퍼 길이일 수 있어 데이터 의미/전체 유효 길이를 증명하지 않습니다.");
        return text.ToString();
    }

    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle,uint code,byte[] input,uint inputSize,byte[] output,uint outputSize,out uint returned,IntPtr overlapped);


    internal static string DecodeCurve(bool ok, uint returned, byte[] bytes)
    {
        if (!ok || returned < 16 || bytes.Length < 16) return "N/A: DSTS 실패 또는 짧은 커브 응답";
        if (bytes.Take(16).All(b => b == 255) || bytes.Take(16).All(b => b == 0))
            return "N/A: 미지원/빈 커브 응답";
        var text = new StringBuilder();
        bool invalid = false;
        for (int i = 0; i < 8; i++)
        {
            int temp = bytes[i], percent = bytes[i + 8];
            invalid |= percent > 100 || temp > 110;
            text.AppendLine($"  {i + 1}: 원본 {temp} °C / {percent}" + (percent > 100 ? " [백분율 의미 미확인]" : " %"));
        }
        text.AppendLine(invalid ? "쓰기 불가: 원본 값 의미 미확인; 255/251을 100%로 보정하지 않음." : "원본 범위 정상; 안전한 쓰기·실효 readback 검증을 뜻하지 않음.");
        return text.ToString();
    }
}
