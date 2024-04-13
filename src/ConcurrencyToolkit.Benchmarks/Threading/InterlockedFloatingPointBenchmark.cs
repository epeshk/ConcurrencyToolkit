using BenchmarkDotNet.Attributes;
using ConcurrencyToolkit.Threading;

namespace ConcurrencyToolkit.Benchmarks.Threading;

public class InterlockedFloatingPointBenchmark
{
  private int N = 4097;

  private double d;
  private float f;

  private int expectedResult;

  public InterlockedFloatingPointBenchmark()
  {
    expectedResult = N * (N - 1) / 2;
    ArgumentOutOfRangeException.ThrowIfNotEqual(IncrementDouble(), expectedResult);
    ArgumentOutOfRangeException.ThrowIfNotEqual(IncrementFloat(), expectedResult);
  }

  [Benchmark]
  public double IncrementDouble()
  {
    d = 0;
    for (int i = 0; i < N; i++)
      InterlockedFloatingPoint.GetAndAdd(ref d, (double)i);

    return d;
  }

  [Benchmark]
  public float IncrementFloat()
  {
    f = 0;
    for (int i = 0; i < N; i++)
      InterlockedFloatingPoint.GetAndAdd(ref f, (float)i);

    return f;
  }
}