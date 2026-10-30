using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GHelper.Fan;

// One request per process. No paths, command lines, or XML cross the pipe.
internal static class NpuBroker
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint pid);
    internal const string Argument = "--internal-npu-broker-v1";
    private const string Prefix = "GHelper-Npu-v1-";
    private const int TokenChars = 64;
    private static bool ValidToken(string s)
    {
        if (s.Length != TokenChars) return false;
        foreach (char c in s) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
        return true;
    }

    internal static Task<NpuClockReading> QueryAsync() => Task.Run(QueryCoreAsync);

    private static async Task<NpuClockReading> QueryCoreAsync()
    {
        // Never launch another broker from its own entrypoint.
        if (Environment.GetCommandLineArgs().Length > 1 && Environment.GetCommandLineArgs()[1] == Argument) return default;
        string token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        using var pipe = new NamedPipeServerStream(Prefix + token, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        BrokerProcess? contained = null;
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return default;
            contained = BrokerProcess.Start(exe, Argument, token, Environment.ProcessId);
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint clientPid) || clientPid != contained.ProcessId) return default;
            byte[] response = new byte[TokenChars + 8];
            await pipe.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(response.AsSpan(0, TokenChars), Encoding.ASCII.GetBytes(token))) return default;
            byte[] extra = new byte[1];
            if (await pipe.ReadAsync(extra, timeout.Token).ConfigureAwait(false) != 0) return default;
            if (contained.WaitForExitCode(timeout.Token) != 0) return default;
            int current = BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(TokenChars, 4));
            int maximum = BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(TokenChars + 4, 4));
            if (current < 0 || maximum < 0) return default;
            return new(current == 0 ? null : current, maximum == 0 ? null : maximum);
        }
        catch (Exception e) when (e is OperationCanceledException || e is System.IO.IOException || e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is UnauthorizedAccessException) { return default; }
        finally
        {
            contained?.Dispose();
        }
    }

    internal static async Task<int> RunChildAsync(string[] args)
    {
        if (args.Length != 3 || args[0] != Argument || !ValidToken(args[1]) || !uint.TryParse(args[2], out uint parentPid) || parentPid == 0) return 2;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            using var pipe = new NamedPipeClientStream(".", Prefix + args[1], PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverPid) || serverPid != parentPid) return 1;
            NpuClockReading reading = NpuClockReader.Parse(await NpuClockReader.QuerySystemAsync(timeout.Token).ConfigureAwait(false));
            byte[] response = new byte[TokenChars + 8];
            Encoding.ASCII.GetBytes(args[1], response);
            BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(TokenChars, 4), reading.CurrentMHz ?? 0);
            BinaryPrimitives.WriteInt32LittleEndian(response.AsSpan(TokenChars + 4, 4), reading.MaxMHz ?? 0);
            await pipe.WriteAsync(response, timeout.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception e) when (e is OperationCanceledException || e is System.IO.IOException || e is UnauthorizedAccessException || e is InvalidOperationException) { return 1; }
    }
}

// Owns a non-inheritable kill-on-close job and the original broker process handle.
internal sealed class BrokerProcess : IDisposable
{
 [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct STARTUPINFO { public int cb; public IntPtr reserved, desktop, title; public int x,y,xSize,ySize,xCount,yCount,fill,flags; public short show,reserved2; public IntPtr reservedPtr, input, output, error; }
 [StructLayout(LayoutKind.Sequential)] internal struct STARTUPINFOEX { public STARTUPINFO startup; public IntPtr attributes; }
 [StructLayout(LayoutKind.Sequential)] internal struct PROCESS_INFORMATION { public IntPtr process, thread; public uint processId, threadId; }
 [StructLayout(LayoutKind.Sequential)] internal struct BASIC_LIMIT { public long processTime, jobTime; public uint flags; public UIntPtr minimumWorkingSet, maximumWorkingSet; public uint activeProcessLimit; public UIntPtr affinity; public uint priorityClass, schedulingClass; }
 [StructLayout(LayoutKind.Sequential)] internal struct IO_COUNTERS { public ulong readOps,writeOps,otherOps,readBytes,writeBytes,otherBytes; }
 [StructLayout(LayoutKind.Sequential)] internal struct EXT_LIMIT { public BASIC_LIMIT basic; public IO_COUNTERS io; public UIntPtr processMemory,jobMemory,peakProcessMemory,peakJobMemory; }

 [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr CreateJobObjectW(IntPtr a, string? name);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int cls,ref EXT_LIMIT info,uint size);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,int flags,ref nuint size);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,nuint attribute,IntPtr value,nuint size,IntPtr previous,IntPtr returnSize);
 [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool CreateProcessW(string app,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string? dir,ref STARTUPINFOEX si,out PROCESS_INFORMATION pi);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
 private IntPtr job, process;
 internal uint ProcessId { get; private set; }
 private static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
 internal static BrokerProcess Start(string exe,string argument,string token,int parent) {
  var owner=new BrokerProcess(); IntPtr list=IntPtr.Zero, jobs=IntPtr.Zero; bool initialized=false; PROCESS_INFORMATION pi=default;
  try {
   owner.job=CreateJobObjectW(IntPtr.Zero,null); Check(owner.job!=IntPtr.Zero);
   var limit=new EXT_LIMIT(); limit.basic.flags=0x2000; Check(SetInformationJobObject(owner.job,9,ref limit,(uint)Marshal.SizeOf<EXT_LIMIT>()));
   nuint size=0; InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size); Check(size!=0);
   list=Marshal.AllocHGlobal(checked((int)size)); Check(InitializeProcThreadAttributeList(list,1,0,ref size)); initialized=true;
   jobs=Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(jobs,owner.job);
   Check(UpdateProcThreadAttribute(list,0,0x2000d,jobs,(nuint)IntPtr.Size,IntPtr.Zero,IntPtr.Zero));
   var si=new STARTUPINFOEX(); si.startup.cb=Marshal.SizeOf<STARTUPINFOEX>(); si.attributes=list;
   var command=new StringBuilder("\""+exe+"\" "+argument+" "+token+" "+parent.ToString(System.Globalization.CultureInfo.InvariantCulture));
   Check(CreateProcessW(exe,command,IntPtr.Zero,IntPtr.Zero,false,0x00080000|0x08000000,IntPtr.Zero,null,ref si,out pi));
   owner.process=pi.process; owner.ProcessId=pi.processId; CloseHandle(pi.thread); return owner;
  } catch { owner.Dispose(); throw; }
  finally { if(initialized) DeleteProcThreadAttributeList(list); if(list!=IntPtr.Zero) Marshal.FreeHGlobal(list); if(jobs!=IntPtr.Zero) Marshal.FreeHGlobal(jobs); }
 }
 internal uint WaitForExitCode(CancellationToken cancellation) {
  using var signal = new EventWaitHandle(false, EventResetMode.ManualReset);
  signal.SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(process, ownsHandle:false);
  if (WaitHandle.WaitAny(new[] { signal, cancellation.WaitHandle }) != 0) cancellation.ThrowIfCancellationRequested();
  Check(GetExitCodeProcess(process, out uint code));
  return code;
 }
 public void Dispose() { if(job!=IntPtr.Zero) { CloseHandle(job); job=IntPtr.Zero; } if(process!=IntPtr.Zero) { CloseHandle(process); process=IntPtr.Zero; } }
}
