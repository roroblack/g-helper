namespace GHelper.Power;

internal enum VendorPowerRoute { Unknown, Excluded, CandidateOnly }

// Static interpretation of the installed ThrottlePlugin 5c467b86... branch,
// NOT live context values, hardware support, authorization or a write API.
internal enum PowerCapabilityBlocker { SkuRoute, Units, Bounds, EffectiveReadback, Rollback, Authorization }

internal static class N1xPowerCapability
{
    internal static IReadOnlyList<PowerCapabilityBlocker> MissingEvidence { get; } =
        Array.AsReadOnly(Enum.GetValues<PowerCapabilityBlocker>());

    internal static string DescribeBlockers() =>
        "N1X 실제 적용 미구현: 후보 0x001200A3(request+596)/0x001200A0(request+600). " +
        "SKU route Unknown: OverclockType(+5680), ProductLine(+6197), CPU vendor(+6128) 실측 미확정. " +
        "단위·SKU/AC/DC별 bounds 미확정. DSTS 지원 확인은 현재 제한값/effective readback 아님. " +
        "실효 조회·캡처 원본 원복·호출 권한 미검증. 저장은 적용이 아닙니다. 하드웨어 변경 없음.";

    // +5680 is logged as OverclockType; +6197 is the low byte of the
    // ProductLine property, NOT CPU vendor (+6128) or a GHelper mode.
    // Evidence: 0x1800158ac, 0x180027394..42c, 0x180016a24..50/b9c.
    internal static VendorPowerRoute EvaluateVendorRoute(
        int? overclockType, int? productLine, int? cpuVendor, int? vendorRequestMode)
    {
        if (overclockType is 3 or 4 ||
            (productLine.HasValue && productLine != 1) ||
            (cpuVendor.HasValue && cpuVendor != 4) ||
            (vendorRequestMode.HasValue && vendorRequestMode is not (4 or 9)))
            return VendorPowerRoute.Excluded;
        if (!overclockType.HasValue || !productLine.HasValue ||
            !cpuVendor.HasValue || !vendorRequestMode.HasValue)
            return VendorPowerRoute.Unknown;
        return VendorPowerRoute.CandidateOnly;
    }
}
