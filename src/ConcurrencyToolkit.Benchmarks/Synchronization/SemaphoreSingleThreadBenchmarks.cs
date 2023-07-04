using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ConcurrencyToolkit.Synchronization;

namespace ConcurrencyToolkit.Benchmarks.Synchronization;

[SimpleJob(RuntimeMoniker.Net80)]
// [SimpleJob(RuntimeMoniker.NativeAot80)]
[MemoryDiagnoser]
[WarmupCount(32)]
[MaxIterationCount(32)]
public class SemaphoreSingleThreadBenchmarks : SemaphoreBenchmarkBase
{
  private const int BATCH_SIZE = 1024 * 1024;

  private CancellationTokenSource cancellationTokenSource = new();
  private ISemaphore semaphore;

  [GlobalSetup]
  public void Setup()
  {
    semaphore = SemaphoreFactory.Create(1);
  }

  [Benchmark]
  public void BenchmarkSemaphore()
  {
    var semaphore = this.semaphore;

    var toDo = 0;
    while (toDo < BATCH_SIZE)
    {
      semaphore.AcquireAsync().GetAwaiter().GetResult();
      var task2 = semaphore.AcquireAsync();
      semaphore.Release();
      task2.GetAwaiter().GetResult();
      semaphore.Release();
      toDo++;
    }
  }

  [Benchmark]
  public void BenchmarkSemaphore_WithToken()
  {
    var semaphore = this.semaphore;
    var token = cancellationTokenSource.Token;

    var toDo = 0;
    while (toDo < BATCH_SIZE)
    {
      semaphore.AcquireAsync(token).GetAwaiter().GetResult();
      var task2 = semaphore.AcquireAsync(token);
      semaphore.Release();
      task2.GetAwaiter().GetResult();
      semaphore.Release();
      toDo++;
    }
  }

  [Benchmark]
  public void BenchmarkSemaphore_Counter()
  {
    var semaphore = this.semaphore;

    var toDo = 0;
    while (toDo < BATCH_SIZE)
    {
      semaphore.AcquireAsync().GetAwaiter().GetResult();
      semaphore.Release();
      semaphore.AcquireAsync().GetAwaiter().GetResult();
      semaphore.Release();
      toDo++;
    }
  }

  [Benchmark]
  public void BenchmarkSemaphore_Counter_WithToken()
  {
    var semaphore = this.semaphore;
    var token = cancellationTokenSource.Token;

    var toDo = 0;
    while (toDo < BATCH_SIZE)
    {
      semaphore.AcquireAsync(token).GetAwaiter().GetResult();
      semaphore.Release();
      semaphore.AcquireAsync(token).GetAwaiter().GetResult();
      semaphore.Release();
      toDo++;
    }
  }
}