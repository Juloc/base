using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;

namespace Base.Web;

/// <summary>
/// Gives memory back once an app has gone quiet after work (0.4, step 7). The .NET collector runs only when new memory is asked for, so an app that did a burst
/// of work (a sign-in, an import, a page full of data) and then sits idle keeps that garbage for hours. Once a minute this looks at how much was allocated:
/// after at least <see cref="MinWork"/> since the last trim and almost nothing in the last minute, it runs one compacting collection (the large object heap
/// included) and asks glibc to return free pages. Measured on caddy: the idle app went from about 110 MB to about 50 MB of its own memory.
/// </summary>
public sealed class IdleTrim : BackgroundService
{
    public static readonly TimeSpan Every = TimeSpan.FromMinutes(1);
    /// <summary>Less than this since the last trim is not worth a collection.</summary>
    public const long MinWork = 8 << 20;
    /// <summary>More than this in the last minute means the app is busy: the collector runs by itself then.</summary>
    public const long QuietBelow = 1 << 20;

    /// <summary>The decision, apart from the clock: bytes allocated since the last trim and in the last minute.</summary>
    public static bool ShouldTrim(long sinceTrim, long lastMinute) => sinceTrim >= MinWork && lastMinute < QuietBelow;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        long atTrim = GC.GetTotalAllocatedBytes(), last = atTrim;
        using var timer = new PeriodicTimer(Every);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var now = GC.GetTotalAllocatedBytes();
                if (ShouldTrim(now - atTrim, now - last)) { Trim(); atTrim = now = GC.GetTotalAllocatedBytes(); }
                last = now;
            }
        }
        catch (OperationCanceledException) { }
    }

    public static void Trim()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        if (OperatingSystem.IsLinux()) try { MallocTrim(0); } catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException) { }
    }

    [DllImport("libc", EntryPoint = "malloc_trim")] private static extern int MallocTrim(nint pad);
}
