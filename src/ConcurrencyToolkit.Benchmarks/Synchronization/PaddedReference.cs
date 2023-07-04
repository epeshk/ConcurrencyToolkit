using System.Runtime.InteropServices;

namespace ConcurrencyToolkit.Benchmarks.Synchronization;

[StructLayout(LayoutKind.Sequential, Pack = 8)]
internal struct PaddedReference<T> where T : class
{
  public T? Object;
  private Padding padding;

  [StructLayout(LayoutKind.Sequential)]
  private struct Padding
  {
    public long a;
    public long b;
    public long c;
    public long d;
    public long e;
    public long f;
    public long g;
  }
}