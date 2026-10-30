using System;
using System.Buffers.Binary;

namespace GHelper.Fan;

// Serialization/evidence contract only. This type never opens a device or grants
// Apply permission. N1xFanPowerPolicy remains the hardware write boundary.
// Installed helper SetFanCurveTable 0x180009768; see follow-up byte-verified trace.
internal static class N1xFanApplyContract
{
    internal sealed record CurveWriteReceipt(bool TransportSucceeded, uint ReturnedBytes, string Status)
    {
        // Setter returns DeviceIoControl BOOL only. No curve ACK schema/effective
        // getter is established; even a four-byte value 1 is NOT curve ACK.
        public bool FirmwareAcknowledged => false;
        public bool EffectiveVerified => false;
    }

    internal static CurveWriteReceipt ClassifyCurveWrite(bool ioSucceeded, uint returnedBytes, byte[] output)
    {
        ArgumentNullException.ThrowIfNull(output);
        string status = !ioSucceeded ? "TransportRejected" :
            returnedBytes > output.Length ? "InvalidTransportEnvelope" : "TransportAcceptedUnverified";
        return new(ioSucceeded, returnedBytes, status);
    }

    internal sealed record RestoreCandidateComparison(bool BytesEqual)
    {
        // No established current-override getter or reset semantics. Matching
        // default/profile bytes cannot establish restored effective firmware state.
        public bool EffectiveVerified => false;
        public bool RestoreVerified => false;
    }

    internal static RestoreCandidateComparison CompareRestoreCandidate(byte[] capturedRaw, byte[] observedRaw)
    {
        ArgumentNullException.ThrowIfNull(capturedRaw);
        ArgumentNullException.ThrowIfNull(observedRaw);
        if (capturedRaw.Length != 16 || observedRaw.Length != 16)
            throw new ArgumentException("Compare exactly sixteen raw bytes without UI relocation.");
        return new(capturedRaw.AsSpan().SequenceEqual(observedRaw));
    }

    // No mode selector in DEVS. Preserve bytes exactly, including unknown values;
    // this builder does not validate thermal safety or turn defaults into a baseline.
    internal static byte[] BuildCurveRequest(int fan, byte[] raw)
    {
        if (fan is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(fan));
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Length != 16) throw new ArgumentException("Exactly sixteen raw bytes required.", nameof(raw));
        byte[] request = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(request, 0x53564544); // DEVS
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(4), 20);
        BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(8), 0x00110024u + (uint)fan);
        raw.CopyTo(request, 12);
        return request;
    }
}
