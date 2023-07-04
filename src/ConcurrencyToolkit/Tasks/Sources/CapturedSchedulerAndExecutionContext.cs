// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;

namespace ConcurrencyToolkit.Tasks.Sources;

/// <summary>A tuple of both a non-null scheduler and a non-null ExecutionContext.</summary>
internal sealed class CapturedSchedulerAndExecutionContext
{
  internal readonly object _scheduler;
  internal readonly ExecutionContext _executionContext;

  public CapturedSchedulerAndExecutionContext(object scheduler, ExecutionContext executionContext)
  {
    Debug.Assert(scheduler is SynchronizationContext or TaskScheduler, $"{nameof(scheduler)} is {scheduler}");
    Debug.Assert(executionContext is not null, $"{nameof(executionContext)} is null");

    _scheduler = scheduler;
    _executionContext = executionContext;
  }
}