// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Metrics;

namespace ConcurrencyToolkit.Synchronization.Internal;

internal sealed class TaskWaiter<TData>
  : TaskCompletionSource<bool>
  where TData : struct
{
  private readonly bool silentCancellation;
  public static readonly TaskWaiter<TData> Canceled;

  static TaskWaiter()
  {
    Canceled = new(false, false);
    Canceled.SetCanceled();
  }

  public TaskWaiter(bool async, bool silentCancellation) : base(
    async ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None)
  {
    this.silentCancellation = silentCancellation;
    Counter<TaskCompletionAllocated>.Increment();
  }

  private TData closure;

  public ref TData Closure => ref closure;

  public bool TryResume() => TrySetResult(true);

  public bool TryCancel(CancellationToken token) => silentCancellation
    ? TrySetResult(false)
    : TrySetCanceled(token);
}