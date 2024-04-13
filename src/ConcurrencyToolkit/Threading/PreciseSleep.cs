using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ConcurrencyToolkit.Threading;

internal static class PreciseSleep
{
  public static void Sleep(int scale)
  {
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
      WindowsPreciseSleep.SleepOnce();
    else
      Nanosleep.Sleep(TimeSpan.FromMilliseconds(scale * 0.05));
  }
}

[SupportedOSPlatform("windows")]
internal static partial class WindowsPreciseSleep
{
  private const int MinimumGranularity = 5000;
  private static readonly long GranularityNs;

  public static void SleepOnce()
  {
    long delayInterval = -1; // Next tick
    NtSetTimerResolution((int)(GranularityNs / 1000), true, out _);
    NtDelayExecution(false, ref delayInterval);
  }

  static WindowsPreciseSleep()
  {
    NtQueryTimerResolution(out _, out int min, out int curr);

    if (min > 0)
    {
      min = Math.Max(min, MinimumGranularity);

      GranularityNs = min * 100L;
      NtSetTimerResolution(min, true, out _);
    }
    else
    {
      GranularityNs = curr * 100L;
    }
  }

  [LibraryImport("ntdll.dll", SetLastError = true)]
  private static partial int NtSetTimerResolution(int DesiredResolution, [MarshalAs(UnmanagedType.Bool)] bool SetResolution, out int CurrentResolution);

  [LibraryImport("ntdll.dll", SetLastError = true)]
  private static partial int NtQueryTimerResolution(out int MaximumResolution, out int MinimumResolution, out int CurrentResolution);

  [LibraryImport("ntdll.dll", SetLastError = true)]
  private static partial uint NtDelayExecution([MarshalAs(UnmanagedType.Bool)] bool Alertable, ref long DelayInterval);
}

internal static partial class Nanosleep
{
  private const uint OverheadNs = 50_000;

  [StructLayout(LayoutKind.Sequential)]
  private struct Timespec
  {
    public long tv_sec;  // Seconds
    public long tv_nsec; // Nanoseconds
  }

  public static void Sleep(TimeSpan sleepTime)
  {
    sleepTime -= TimeSpan.FromTicks(OverheadNs / 100);
    if (sleepTime < TimeSpan.Zero)
      sleepTime = TimeSpan.Zero;
    var req = GetTimespecFromNanoseconds((ulong)sleepTime.Ticks * 100);
    var rem = new Timespec();
    nanosleep(ref req, ref rem);
  }

  [LibraryImport("libc", SetLastError = true)]
  private static partial int nanosleep(ref Timespec req, ref Timespec rem);

  /// <summary>
  /// Convert a timeout in nanoseconds to a timespec for nanosleep.
  /// </summary>
  /// <param name="nanoseconds">Timeout in nanoseconds</param>
  /// <returns>Timespec for nanosleep</returns>
  private static Timespec GetTimespecFromNanoseconds(ulong nanoseconds)
  {
    return new Timespec
    {
      tv_sec = (long)(nanoseconds / 1_000_000_000),
      tv_nsec = (long)(nanoseconds % 1_000_000_000)
    };
  }
}