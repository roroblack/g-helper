namespace GHelper.Fan;

// Owned by the reader gate. Snapshots contain immutable rows, never mutable dictionaries.
internal sealed class ThermalZoneState
{
    private readonly Dictionary<string,ThermalZoneSample> rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string,(ulong Timestamp,long ChangedAt)> epochs = new(StringComparer.Ordinal);
    internal void Accept(ThermalZoneSample[] samples, long now)
    {
        var groups=samples.Where(x=>!string.IsNullOrWhiteSpace(x.Name)).GroupBy(x=>x.Name,StringComparer.Ordinal).ToDictionary(x=>x.Key,x=>x.ToArray(),StringComparer.Ordinal);
        foreach(var key in rows.Keys.Except(groups.Keys).ToArray()) { rows[key]=rows[key] with { Celsius=null, Status="zone-missing" }; epochs.Remove(key); }
        foreach(var (key,group) in groups)
        {
            var row=group[0];
            if(group.Length!=1) row=row with { Celsius=null,Status="ambiguous-instance" };
            if(row.ProviderTimestamp is ulong ts)
            {
                if(!epochs.TryGetValue(key,out var epoch) || epoch.Timestamp!=ts) epochs[key]=(ts,now); // reset/backwards establishes a new epoch too
                else if(row.Celsius is not null && now-epoch.ChangedAt>5000) row=row with { Celsius=null, Status="stale-provider" };
            }
            else epochs.Remove(key); // firmware freshness remains unknown even with provider timestamps
            rows[key]=row;
        }
    }
    internal void Fail(string status)
    {
        if(rows.Count==0) foreach(var name in new[]{@"\_TZ.THRM",@"\_TZ.THZ0"}) rows[name]=ThermalZoneSample.Decode(name,null,null,DateTimeOffset.MinValue,0);
        foreach(var key in rows.Keys.ToArray()) rows[key]=rows[key] with { Celsius=null, Status=status };
    }
    internal ThermalZoneSample[] Snapshot(long now) => rows.Values.OrderBy(x=>x.Name,StringComparer.Ordinal)
        .Select(x=>x.Celsius is not null && now-x.AcquiredAtMonotonic>5000 ? x with { Celsius=null,Status="stale-acquisition" } : x).ToArray();
    internal void Reset() { Fail("reset/suspend; acquisition pending"); epochs.Clear(); }
}
