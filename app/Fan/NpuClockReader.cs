using System;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace GHelper.Fan;

internal readonly record struct NpuClockReading(int? CurrentMHz, int? MaxMHz);

internal sealed class NpuClockReader
{
    private static readonly SemaphoreSlim Flight = new(1, 1);
    private readonly Func<Task<NpuClockReading>> query;
    internal NpuClockReader() => query = NpuBroker.QueryAsync;
    internal NpuClockReader(Func<Task<string?>> query) => this.query = async () => Parse(await query().ConfigureAwait(false));

    internal async Task<NpuClockReading> ReadAsync()
    {
        if (!await Flight.WaitAsync(0).ConfigureAwait(false)) return default;
        try { return await query().ConfigureAwait(false); }
        catch { return default; }
        finally { Flight.Release(); }
    }

    internal static NpuClockReading Parse(string? xml)
    {
        if (string.IsNullOrEmpty(xml) || xml.Length > 1024 * 1024) return default;
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024
            });
            var root = XDocument.Load(reader).Root;
            if (root?.Name != "nvidia_smi_log") return default;
            var candidates = root.Elements("gpu").Where(g =>
                ((string?)g.Element("product_name"))?.Trim() == "NVIDIA NPU" ||
                ((string?)g.Element("product_brand"))?.Trim() == "NPU" ||
                (((string?)g.Element("uuid"))?.Trim().StartsWith("DLA-", StringComparison.Ordinal) ?? false)).ToArray();
            if (candidates.Length != 1 ||
                candidates[0].Elements("product_name").Count() != 1 ||
                candidates[0].Elements("product_brand").Count() != 1 ||
                candidates[0].Elements("uuid").Count() != 1 ||
                ((string?)candidates[0].Element("product_name"))?.Trim() != "NVIDIA NPU" ||
                ((string?)candidates[0].Element("product_brand"))?.Trim() != "NPU" ||
                !IsDlaUuid(((string?)candidates[0].Element("uuid"))?.Trim())) return default;
            var clocks = candidates[0].Elements("dla_clocks").ToArray();
            if (clocks.Length != 1) return default;
            return new(ParseClock(clocks[0], "dla_cur_engine_clock"), ParseClock(clocks[0], "dla_max_engine_clock"));
        }
        catch (Exception e) when (e is XmlException || e is InvalidOperationException || e is ArgumentException) { return default; }
    }

    private static bool IsDlaUuid(string? value) => value != null && value.StartsWith("DLA-", StringComparison.Ordinal) && Guid.TryParseExact(value.Substring(4), "D", out _);
    private static int? ParseClock(XElement parent, string name)
    {
        var elements = parent.Elements(name).ToArray();
        if (elements.Length != 1 || elements[0].HasElements) return null;
        string s = elements[0].Value.Trim();
        if (!s.EndsWith(" MHz", StringComparison.Ordinal)) return null;
        return int.TryParse(s.AsSpan(0, s.Length - 4), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : null;
    }

    internal static Task<string?> QuerySystemAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe");
        return Contained.Run(path, "-q -x", TimeSpan.FromSeconds(3), 1024 * 1024, cancellationToken: cancellationToken);
    }
}

static class Contained {
 const uint INHERIT=1, STARTF_USESTDHANDLES=0x100, EXTENDED=0x80000, NO_WINDOW=0x08000000, KILL_ON_CLOSE=0x2000;
 const nuint HANDLE_LIST=0x20002, JOB_LIST=0x2000d;
 [StructLayout(LayoutKind.Sequential)] internal struct SECURITY_ATTRIBUTES { public int length; public IntPtr descriptor; public int inherit; }
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct STARTUPINFO { public int cb; public IntPtr reserved, desktop, title; public int x,y,xSize,ySize,xCount,yCount,fill,flags; public short show,reserved2; public IntPtr reservedPtr, input, output, error; }
 [StructLayout(LayoutKind.Sequential)] internal struct STARTUPINFOEX { public STARTUPINFO startup; public IntPtr attributes; }
 [StructLayout(LayoutKind.Sequential)] internal struct PROCESS_INFORMATION { public IntPtr process, thread; public uint processId, threadId; }
 [StructLayout(LayoutKind.Sequential)] internal struct BASIC_LIMIT { public long processTime, jobTime; public uint flags; public UIntPtr minimumWorkingSet, maximumWorkingSet; public uint activeProcessLimit; public UIntPtr affinity; public uint priorityClass, schedulingClass; }
 [StructLayout(LayoutKind.Sequential)] internal struct IO_COUNTERS { public ulong readOps,writeOps,otherOps,readBytes,writeBytes,otherBytes; }
 [StructLayout(LayoutKind.Sequential)] internal struct EXT_LIMIT { public BASIC_LIMIT basic; public IO_COUNTERS io; public UIntPtr processMemory,jobMemory,peakProcessMemory,peakJobMemory; }
 [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job, int cls, ref EXT_LIMIT info, uint size);

 [DllImport("kernel32.dll", SetLastError=true)] static extern bool CreatePipe(out IntPtr read, out IntPtr write, ref SECURITY_ATTRIBUTES sa, uint size);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetHandleInformation(IntPtr h, uint mask, uint flags);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returnSize);
 [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CreateProcessW(string app, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string? directory, ref STARTUPINFOEX startup, out PROCESS_INFORMATION process);

 [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
 [DllImport("kernel32.dll", SetLastError=true)] static extern uint WaitForSingleObject(IntPtr h, uint ms);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool CloseHandle(IntPtr h);
 static void Check(bool b) { if (!b) throw new Win32Exception(Marshal.GetLastWin32Error()); }
 static void Close(ref IntPtr h) { if(h != IntPtr.Zero && h != new IntPtr(-1)) CloseHandle(h); h=IntPtr.Zero; }
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool PeekNamedPipe(IntPtr pipe, IntPtr buffer, uint size, IntPtr read, out uint available, IntPtr left);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadFile(IntPtr pipe, byte[] buffer, uint count, out uint read, IntPtr overlapped);
 // Peek before reading: exactly one reader owns each pipe, so this never waits for more data.
 // There is no background blocking reader to abandon when a descendant retains a writer.
 static bool Pump(IntPtr pipe, MemoryStream sink, int limit, out bool eof) {
  eof=false;
  if(!PeekNamedPipe(pipe,IntPtr.Zero,0,IntPtr.Zero,out uint available,IntPtr.Zero)) {
   int error=Marshal.GetLastWin32Error(); if(error==109) { eof=true; return true; } throw new Win32Exception(error);
  }
  if(available==0) return true;
  if((long)available+sink.Length>limit) return false;
  var buffer=new byte[Math.Min((int)available,4096)];
  Check(ReadFile(pipe,buffer,(uint)buffer.Length,out uint n,IntPtr.Zero)); sink.Write(buffer,0,(int)n); return true;
 }
 internal static async Task<string?> Run(string exe, string arguments, TimeSpan timeout, int limit, bool failAssign=false, CancellationToken cancellationToken=default) {
  var clock=System.Diagnostics.Stopwatch.StartNew();
  if(timeout <= TimeSpan.Zero || limit < 0) throw new ArgumentOutOfRangeException();
  bool Expired() => cancellationToken.IsCancellationRequested || clock.Elapsed >= timeout;
  if(Expired()) return null;
  IntPtr job=IntPtr.Zero, or=IntPtr.Zero, ow=IntPtr.Zero, er=IntPtr.Zero, ew=IntPtr.Zero, ir=IntPtr.Zero, iw=IntPtr.Zero, list=IntPtr.Zero, handles=IntPtr.Zero, jobs=IntPtr.Zero; PROCESS_INFORMATION pi=default; bool initialized=false;
  try {
   job=CreateJobObjectW(IntPtr.Zero,null); Check(job!=IntPtr.Zero); Check(SetHandleInformation(job, INHERIT,0));
   var limits=new EXT_LIMIT(); limits.basic.flags=KILL_ON_CLOSE; Check(SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<EXT_LIMIT>()));
   var sa=new SECURITY_ATTRIBUTES { length=Marshal.SizeOf<SECURITY_ATTRIBUTES>(), inherit=1 };
   Check(CreatePipe(out or,out ow,ref sa,0)); Check(SetHandleInformation(or,INHERIT,0));
   Check(CreatePipe(out er,out ew,ref sa,0)); Check(SetHandleInformation(er,INHERIT,0));
   Check(CreatePipe(out ir,out iw,ref sa,0)); Check(SetHandleInformation(iw,INHERIT,0)); Close(ref iw);
   nuint size=0; InitializeProcThreadAttributeList(IntPtr.Zero,2,0,ref size); if(size==0) throw new Win32Exception(Marshal.GetLastWin32Error());
   list=Marshal.AllocHGlobal(checked((int)size)); Check(InitializeProcThreadAttributeList(list,2,0,ref size)); initialized=true;
   handles=Marshal.AllocHGlobal(IntPtr.Size*3); Marshal.WriteIntPtr(handles,0,ir); Marshal.WriteIntPtr(handles,IntPtr.Size,ow); Marshal.WriteIntPtr(handles,2*IntPtr.Size,ew);
   Check(UpdateProcThreadAttribute(list,0,HANDLE_LIST,handles,(nuint)(IntPtr.Size*3),IntPtr.Zero,IntPtr.Zero));
   jobs=Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(jobs,job);
   if(failAssign) return null;
   Check(UpdateProcThreadAttribute(list,0,JOB_LIST,jobs,(nuint)IntPtr.Size,IntPtr.Zero,IntPtr.Zero));
   var si=new STARTUPINFOEX(); si.startup.cb=Marshal.SizeOf<STARTUPINFOEX>(); si.startup.flags=(int)STARTF_USESTDHANDLES; si.startup.input=ir; si.startup.output=ow; si.startup.error=ew; si.attributes=list;
   // The executable is passed separately; callers supply only pre-quoted arguments.
   var cmd=new StringBuilder("\""+exe+"\""+(arguments.Length>0?" "+arguments:""));
   if(Expired()) return null;
   Check(CreateProcessW(exe,cmd,IntPtr.Zero,IntPtr.Zero,true,EXTENDED|NO_WINDOW,IntPtr.Zero,null,ref si,out pi));
   Close(ref ow); Close(ref ew); Close(ref ir);
   using var output=new MemoryStream(); using var errors=new MemoryStream();
   bool outEof=false, errEof=false;
   while(true) {
    if(Expired()) return null;
    if(!outEof && !Pump(or,output,limit,out outEof)) return null;
    if(!errEof && !Pump(er,errors,limit,out errEof)) return null;
    uint wait=WaitForSingleObject(pi.process,0); if(wait!=0 && wait!=258) return null;
    if(wait==0 && outEof && errEof) break;
    try { await Task.Delay(10,cancellationToken).ConfigureAwait(false); } catch(OperationCanceledException) { return null; }
   }
   if(Expired() || !GetExitCodeProcess(pi.process,out uint code) || code!=0) return null;
   return new UTF8Encoding(false, true).GetString(output.ToArray());
  } catch(Exception e) when (e is Win32Exception || e is DecoderFallbackException) { return null; }
  finally {

   // Closing the non-inheritable job terminates all contained descendants, even if the root already exited.
   Close(ref job); Close(ref pi.thread); Close(ref pi.process); Close(ref or); Close(ref ow); Close(ref er); Close(ref ew); Close(ref ir); Close(ref iw);
   if(initialized) DeleteProcThreadAttributeList(list); if(list!=IntPtr.Zero) Marshal.FreeHGlobal(list); if(handles!=IntPtr.Zero) Marshal.FreeHGlobal(handles); if(jobs!=IntPtr.Zero) Marshal.FreeHGlobal(jobs);
  }
 }
}
