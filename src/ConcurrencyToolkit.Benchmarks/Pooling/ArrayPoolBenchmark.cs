// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ConcurrencyToolkit.Pooling;

namespace ConcurrencyToolkit.Benchmarks.Pooling;
using static Params;

public static class Params
{
  public const int ArraySize = 1024;
  public const int Threads = 8;
  public const int Iterations = 64*1024;
}

[SimpleJob(RuntimeMoniker.Net80)]
// [SimpleJob(RuntimeMoniker.NativeAot80)]
[MemoryDiagnoser]
[WarmupCount(32)]
[MaxIterationCount(32)]
public class ArrayPoolBenchmark
{
  public record NamedParam(ArrayPool<byte> Value, string DisplayText)
  {
    public override string ToString() => DisplayText;
  }

  private static ArrayPool<byte> shared = ArrayPool<byte>.Shared;
  private static ArrayPool<byte> create = ArrayPool<byte>.Create(int.MaxValue, 100);
  private static ArrayPool<byte> nolock = LocklessArrayPool<byte>.Shared;

  public NamedParam[] Pools { get; } = {
    // array pools
    new(shared, "Shared"),
    new(create, "Create"),
    new(nolock, "NoLock")
  };

  [ParamsSource(nameof(Pools))]
  public NamedParam Pool { get; set; }

  public ArrayPool<byte> ArrayPool => Pool.Value;

  [Benchmark()]
  public void TwoArrays()
  {
    var tasks = new Task[Threads];
    for (int i = 0; i < Threads; i++)
    {
      tasks[i] = Task.Run(() =>
      {
        for (int j = 0; j < Iterations; j++)
        {
          var arr1 = ArrayPool.Rent(ArraySize);
          var arr2 = ArrayPool.Rent(ArraySize);
          Random.Shared.NextBytes(arr1);
          Random.Shared.NextBytes(arr2);
          ArrayPool.Return(arr2);
          ArrayPool.Return(arr1);
        }
      });
    }

    Task.WaitAll(tasks);
  }
}