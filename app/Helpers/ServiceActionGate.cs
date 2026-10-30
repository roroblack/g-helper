namespace GHelper.Helpers;

internal sealed class ServiceActionGate
{
    private int busy;
    public bool Busy => Volatile.Read(ref busy) != 0;
    public bool TryEnter() => Interlocked.CompareExchange(ref busy, 1, 0) == 0;
    public void Exit() => Interlocked.Exchange(ref busy, 0);
}
