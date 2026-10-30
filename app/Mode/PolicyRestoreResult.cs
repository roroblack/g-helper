namespace GHelper.Mode;
internal sealed record PolicyRestoreResult(bool Success, string Detail)
{
    public void EnsureSuccess()
    {
        if (!Success) throw new InvalidOperationException(Detail);
    }
}
