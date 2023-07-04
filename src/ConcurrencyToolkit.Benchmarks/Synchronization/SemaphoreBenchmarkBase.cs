using BenchmarkDotNet.Attributes;
using ConcurrencyToolkit.Synchronization;

namespace ConcurrencyToolkit.Benchmarks.Synchronization;

public abstract class SemaphoreBenchmarkBase
{
  public NamedParam[] SemaphoreFactories { get; } = {
    new(x => new QueueSemaphore(x), "Queue"),
    new(x => new StackSemaphore(x), "Stack"),
    new(x => new SimpleSegmentSemaphore(x), "SimpleSegment"),
    new(x => new SegmentSemaphore(x), "Segment"),
    new(x => new PrioritySemaphore(x, maxPriority: 0).WithPriority(0), "Priority"),
    new(x => new SSW(new(x)), "Slim"),
    new(x => new SemaphoreSlimWrapper(new(x)), "SlimWrapper")
  };

  [ParamsSource(nameof(SemaphoreFactories))]
  public NamedParam SemaphoreFactory { get; set; }

  public record NamedParam(Func<int, ISemaphore> Create, string DisplayText)
  {
    public override string ToString() => DisplayText;
  }
}