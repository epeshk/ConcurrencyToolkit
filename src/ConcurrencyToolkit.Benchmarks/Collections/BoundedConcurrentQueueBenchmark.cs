using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ConcurrencyToolkit.Collections.Queues;

namespace ConcurrencyToolkit.Benchmarks.Collections;

[SimpleJob(RuntimeMoniker.Net80)]
// [SimpleJob(RuntimeMoniker.NativeAot80)]
[MemoryDiagnoser]
[WarmupCount(32)]
[MaxIterationCount(32)]
public class BoundedConcurrentQueueBenchmark
{
  private FixedSizeConcurrentQueue<long> Queue = new(32);

  [Benchmark]
  public void EnqueueingAndDequeueing()
  {
    for (int i = 0; i < 64; i++)
    {
      Queue.TryEnqueue(i);
    }
    for (int i = 0; i < 64; i++)
    {
      Queue.TryDequeue(out _);
    }
  }
}