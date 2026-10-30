using System;
using System.Runtime.CompilerServices;
using GHelper.Fan;

namespace GHelper;

internal static class BrokerEntry
{
    [STAThread]
    internal static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == NpuBroker.Argument)
        {
            Environment.ExitCode = NpuBroker.RunChildAsync(args).GetAwaiter().GetResult();
            return;
        }
        RunApplication(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApplication(string[] args) => Program.Main(args);
}
