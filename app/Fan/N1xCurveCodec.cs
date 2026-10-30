using System;
using System.Buffers.Binary;

namespace GHelper.Fan;

// Codec only: no IOCTL, profile persistence, mode transition or Apply authority.
// Evidence: ProArt FanCurveItemModel.SetPosition/PostionToData; see fan trace report.
// Raw firmware/default tables must NEVER pass through a lossy UI normalization.
internal static class N1xCurveCodec
{
    internal static uint[] RawToWords(byte[] raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length != 16) throw new ArgumentException("Exactly 16 raw bytes required.", nameof(raw));
        uint[] words = new uint[4];
        for (int i = 0; i < 4; i++)
            words[i] = BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(i * 4, 4));
        return words;
    }

    // Vendor UI's edit acceptance policy, NOT proof of model thermal safety.
    // laptop.cs SaveFanCurveChanged: lower bounds + monotonicity + per-index bins.
    // A successful encoding must not grant write permission in N1xFanPowerPolicy.
    internal static byte[] EncodeEdited(int[] celsius, int[] percentages, byte[] lowerLimits)
    {
        ArgumentNullException.ThrowIfNull(celsius);
        ArgumentNullException.ThrowIfNull(percentages);
        ArgumentNullException.ThrowIfNull(lowerLimits);
        if (celsius.Length != 8 || percentages.Length != 8 || lowerLimits.Length != 8)
            throw new ArgumentException("Exactly eight temperatures, percentages and measured lower limits required.");
        byte[] raw = new byte[16];
        for (int i = 0; i < 8; i++)
        {
            if (lowerLimits[i] > 100)
                throw new ArgumentException("Unknown lower-limit encoding; do not infer a safe percentage.", nameof(lowerLimits));
            if (celsius[i] < 30 + i * 10 || celsius[i] > 39 + i * 10)
                throw new ArgumentException("Temperature outside vendor index bin.", nameof(celsius));
            if (percentages[i] < lowerLimits[i] || percentages[i] > 100)
                throw new ArgumentException("Percentage outside vendor edit bounds.", nameof(percentages));
            if (i > 0 && (celsius[i] < celsius[i - 1] || percentages[i] < percentages[i - 1]))
                throw new ArgumentException("Vendor edited curve must be nondecreasing.");
            raw[i] = (byte)celsius[i];
            raw[i + 8] = (byte)percentages[i];
        }
        return raw;
    }

    internal static byte[] WordsToRaw(uint[] words)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (words.Length != 4) throw new ArgumentException("Exactly 4 vendor words required.", nameof(words));
        byte[] raw = new byte[16];
        for (int i = 0; i < 4; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(i * 4, 4), words[i]);
        return raw;
    }
}
