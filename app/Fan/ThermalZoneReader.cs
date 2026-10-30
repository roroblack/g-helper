using System.Management;
using System.Runtime.InteropServices;

namespace GHelper.Fan;

internal sealed class ThermalZoneReader
{
    internal static ThermalZoneReader Shared => SharedHolder.Instance;
    private static class SharedHolder
    {
        internal static readonly ThermalZoneReader Instance = Create();
        private static ThermalZoneReader Create()
        {
            var reader = new ThermalZoneReader();
            Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) => {
                if (e.Mode is Microsoft.Win32.PowerModes.Suspend or Microsoft.Win32.PowerModes.Resume) reader.Reset();
            };
            Application.ApplicationExit += (_, _) => reader.Close();
            return reader;
        }
    }
    private readonly object gate=new();
    private readonly ThermalZoneState state=new();
    private readonly Func<ThermalZoneSample[]> read;
    private readonly Func<long> clock;
    private bool busy, closed, timedOut;
    private long generation, started=long.MinValue;
    private static readonly SemaphoreSlim wmiGate=new(1,1);
    internal bool Busy { get { lock(gate) return busy; } }
    internal ThermalZoneReader(Func<ThermalZoneSample[]>? read=null, Func<long>? clock=null)
    {
        this.read=read??ReadWmi;
        this.clock=clock??(()=>Environment.TickCount64);
        state.Fail("acquisition pending");
    }
    private void Expire(long now)
    {
        if(busy && !timedOut && now-started>2000) { timedOut=true; state.Fail("timeout"); }
    }
    internal ThermalZoneSample[] Peek() { lock(gate) { Expire(clock()); return state.Snapshot(clock()); } }
    internal void Request()
    {
        lock(gate)
        {
            long now=clock(); Expire(now);
            if(closed || busy || (started!=long.MinValue && now-started<2000)) return;
            busy=true; timedOut=false; started=now; long epoch=generation;
            _=Task.Run(()=>
            {
                ThermalZoneSample[] result=[]; string? error=null;
                try { result=read(); }
                catch(Exception ex) { error=Classify(ex); }
                lock(gate)
                {
                    Expire(clock());
                    if(!closed && epoch==generation && !timedOut)
                    {
                        if(error is not null) state.Fail(error);
                        else { var utc=DateTimeOffset.UtcNow; long mono=clock(); state.Accept(result.Select(x=>x with { AcquiredAtUtc=utc,AcquiredAtMonotonic=mono }).ToArray(),mono); }
                    }
                    busy=false;
                }
            });
        }
    }
    internal void Reset() { lock(gate) { generation++; state.Reset(); started=busy?started:long.MinValue; } }
    internal void Close() { lock(gate) { closed=true; generation++;state.Fail("closed"); } }
    internal static string Classify(Exception ex)
    {
        string category=ex switch {
            TimeoutException=>"timeout",
            ManagementException m when m.ErrorCode==ManagementStatus.Timedout=>"timeout",
            UnauthorizedAccessException=>"permission-denied",
            ManagementException m when m.ErrorCode==ManagementStatus.AccessDenied=>"permission-denied",
            ManagementException m when m.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.NotFound or ManagementStatus.ProviderNotFound=>"unavailable",
            COMException c when c.HResult==unchecked((int)0x80070005)=>"permission-denied",
            _=>"query-failed"
        };
        return $"{category}; HRESULT=0x{ex.HResult:X8}; {ex.GetType().Name}: {ex.Message}";
    }
    // One global gate survives panel close/reopen. A timed-out synchronous WMI call
    // cannot be forcibly cancelled; reopening never creates another native worker.
    internal static ThermalZoneSample[] ReadWmi()
    {
        if(!wmiGate.Wait(0)) throw new InvalidOperationException("previous WMI query still in progress");
        try
        {
            using var searcher=new ManagementObjectSearcher(@"root\cimv2", "SELECT Name, Temperature, Timestamp_Sys100NS FROM Win32_PerfRawData_Counters_ThermalZoneInformation");
            searcher.Options.Timeout=TimeSpan.FromSeconds(2);
            using var collection=searcher.Get();
            var rows=new List<ThermalZoneSample>();
            foreach(ManagementObject item in collection)
            {
                using(item)
                {
                    string name=item["Name"] as string??"";
                    if(string.IsNullOrWhiteSpace(name)) continue; // no valid identity to publish
                    object? raw=null,ts=null;
                    try { raw=item["Temperature"]; ts=item["Timestamp_Sys100NS"]; }
                    catch(ManagementException) { } // isolate malformed properties to this zone
                    rows.Add(ThermalZoneSample.Decode(name,raw,ts,DateTimeOffset.UtcNow,Environment.TickCount64));
                }
            }
            return rows.ToArray();
        }
        finally { wmiGate.Release(); }
    }
}
