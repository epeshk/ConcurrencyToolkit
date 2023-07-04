// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;

namespace ConcurrencyToolkit.Synchronization;

[DebuggerDisplay("Count = {CurrentCount}, Queue = {CurrentQueue}")]
public sealed class SemaphoreSlimWrapper : ISemaphore
{
  private readonly SemaphoreSlim semaphoreSlim;
  private volatile int queue;

  public SemaphoreSlimWrapper(SemaphoreSlim semaphoreSlim) => this.semaphoreSlim = semaphoreSlim;

  public ValueTask AcquireAsync(CancellationToken token=default)
  {
    var task = semaphoreSlim.WaitAsync(token);
    if (!task.IsCompleted)
    {
      Interlocked.Increment(ref queue);
      task.ContinueWith(
        (_, state) => Interlocked.Decrement(ref ((SemaphoreSlimWrapper)state).queue), this,
        TaskContinuationOptions.ExecuteSynchronously);
    }
    return new(task);
  }

  public ValueTask<bool> TryAcquireAsync(CancellationToken token=default)
  {
    var canBeCancelled = token.CanBeCanceled;
    var task = semaphoreSlim.WaitAsync(-1, token);
    if (!task.IsCompleted)
    {
      Interlocked.Increment(ref queue);
      task.ContinueWith(static (_, state) => Interlocked.Decrement(ref ((SemaphoreSlimWrapper)state).queue), this, TaskContinuationOptions.ExecuteSynchronously);
    }
    return new(canBeCancelled ? NoThrow(task) : task);
  }

  private static Task<bool> NoThrow(Task<bool> task) =>
    task.ContinueWith(
      static t => t.IsCompletedSuccessfully && t.GetAwaiter().GetResult(),
      TaskContinuationOptions.ExecuteSynchronously);

  public void Acquire(CancellationToken token=default)
  {
    if (semaphoreSlim.Wait(0, token))
      return;

    Interlocked.Increment(ref queue);
    try
    {
      semaphoreSlim.Wait(token);
    }
    finally
    {
      Interlocked.Decrement(ref queue);
    }
  }

  public bool TryAcquire(CancellationToken token=default)
  {

    try
    {
      if (semaphoreSlim.Wait(0, token))
        return true;

      Interlocked.Increment(ref queue);
      try
      {
        Acquire(token);
        return true;
      }
      finally
      {
        Interlocked.Decrement(ref queue);
      }
    }
    catch (OperationCanceledException)
    {
      return false;
    }
  }

  public bool TryAcquireImmediately() => semaphoreSlim.Wait(0);

  public void Release() => semaphoreSlim.Release();

  public int CurrentCount => semaphoreSlim.CurrentCount;
  public int CurrentQueue => queue;
}