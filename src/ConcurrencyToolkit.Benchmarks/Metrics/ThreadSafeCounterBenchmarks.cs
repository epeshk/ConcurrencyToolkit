using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ConcurrencyToolkit.Metrics;

namespace ConcurrencyToolkit.Benchmarks.Metrics;

[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.NativeAot80)]
public class ThreadSafeCounterBenchmarks
{
  private long x;

  [Benchmark]
  public void IncrementSingleThread()
  {
    var counter = new ThreadSafeCounter64();
    for (int i = 0; i < 4096; i++)
      counter.Increment();
  }
  [Benchmark]
  public void IncrementSingleThread_SingleVariable()
  {
    var counter = new ThreadSafeCounter64();
    for (int i = 0; i < 4096; i++)
      Interlocked.Increment(ref x);
  }
  [Benchmark]
  public void IncrementMultiThread()
  {
    var counter = new ThreadSafeCounter64();
    var tasks = new Task[4];
    for (int j = 0; j < tasks.Length; j++)
    {
      tasks[j] = Task.Run(() =>
      {
        for (int i = 0; i < 4096; i++)
          counter.Increment();
      });
    }

    Task.WaitAll(tasks);
  }
  [Benchmark]
  public void IncrementMultiThread_Collision()
  {
    var counter = new ThreadSafeCounter64();
    var tasks = new Task[4];
    for (int j = 0; j < tasks.Length; j++)
    {
      var k = (uint)j;
      tasks[j] = Task.Run(() =>
      {
        ShardingId.Set(64*(k+1));
        for (int i = 0; i < 4096; i++)
          counter.Increment();
      });
    }

    Task.WaitAll(tasks);
  }

  [Benchmark]
  public void IncrementMultiThread_SingleVariable()
  {
    var counter = new ThreadSafeCounter64();
    var tasks = new Task[4];
    for (int j = 0; j < tasks.Length; j++)
    {
      tasks[j] = Task.Run(() =>
      {
        for (int i = 0; i < 4096; i++)
          Interlocked.Increment(ref x);
      });
    }

    Task.WaitAll(tasks);
  }
}

public class ShardingIdBenchmark
{
  [Benchmark]
  public uint Shard() => ShardingId.Current;
  [Benchmark]
  public uint Thread() => (uint)Environment.CurrentManagedThreadId;

  [Benchmark]
  public uint Core() => (uint)System.Threading.Thread.GetCurrentProcessorId();
}
