using System;
using System.Buffers.Binary;

namespace GHelper.Fan;

// H7407BA observations, not a blanket ARM64 capability claim.
internal static class N1xFanPowerPolicy
{
    internal const string Unverified = "N1X: 팬 커브·히스테리시스·CPU 전력 쓰기 미검증 (비활성화)";
    internal static bool IsTarget(string? model) =>
        model?.Contains("H7407BA", StringComparison.OrdinalIgnoreCase) == true;

    // Endpoint existence and historical defaults do not establish writable
    // bounds, fan scaling, persistence or effective readback on this SKU.
    internal static bool BlocksWrite(string? model, uint id) => IsTarget(model) && id is
        0x00110022 or 0x00110023 or 0x00110024 or 0x00110025 or
        0x00110032 or 0x00110034 or 0x00120098 or 0x00120099 or
        0x0012009C or 0x0012009E or 0x0012009F or
        0x001200A0 or 0x001200A1 or 0x001200A2 or 0x001200A3 or
        0x001200B0 or 0x001200B1 or 0x001200C0 or 0x001200C1 or 0x001200C2;

    internal static string FormatFan(int hundredsRpm) =>
        hundredsRpm < 0 ? "N/A" : $"{hundredsRpm * 100}RPM";

    // DSTS returns support bit16 plus speed in hundreds of RPM. Keep that
    // existing GetFan unit contract; never interpret a failed zero buffer as 0RPM.
    internal static int DecodeFan(bool ioSucceeded, uint returned, ReadOnlySpan<byte> buffer)
    {
        if (!ioSucceeded || returned < 4 || buffer.Length < 4) return -1;
        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        if ((raw & 0x10000) == 0 || (raw & 0x80000000) != 0) return -1;
        int speed = (int)(raw & 0xffff);
        return speed <= 120 ? speed : -1;
    }
}
