using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ConcurrencyToolkit.Metrics;

namespace ConcurrencyToolkit.Benchmarks.Metrics;

[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.NativeAot80)]
public class ThreadSafeCounterDoubleBenchmarks
{
  private const double val = Math.PI;

  private double x;
  private double y;

  [Benchmark]
  public void IncrementSingleThread()
  {
    var counter = new ThreadSafeCounter64();
    for (int i = 0; i < 4096; i++)
      counter.Add(val);
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
          counter.Add(val);
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
          counter.Add(val);
      });
    }

    Task.WaitAll(tasks);
  }
}