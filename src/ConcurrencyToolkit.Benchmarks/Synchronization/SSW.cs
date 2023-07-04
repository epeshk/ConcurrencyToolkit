using System.Diagnostics;
using ConcurrencyToolkit.Synchronization;

namespace ConcurrencyToolkit.Benchmarks.Synchronization;

[DebuggerDisplay("Count = {CurrentCount}, Queue = {CurrentQueue}")]
public class SSW : ISemaphore
{
  private readonly SemaphoreSlim semaphoreSlim;

  public SSW(SemaphoreSlim semaphoreSlim) => this.semaphoreSlim = semaphoreSlim;

  public ValueTask AcquireAsync(CancellationToken token=default)
  {
    var task = semaphoreSlim.WaitAsync(token);
    return new(task);
  }

  public ValueTask<bool> TryAcquireAsync(CancellationToken token=default)
  {
    var canBeCancelled = token.CanBeCanceled;
    var task = semaphoreSlim.WaitAsync(-1, token);
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

    semaphoreSlim.Wait(token);
  }

  public bool TryAcquire(CancellationToken token=default)
  {

    try
    {
      if (semaphoreSlim.Wait(0, token))
        return true;

      Acquire(token);
      return true;
    }
    catch (OperationCanceledException)
    {
      return false;
    }
  }

  public bool TryAcquireImmediately() => semaphoreSlim.Wait(0);

  public void Release() => semaphoreSlim.Release();

  public int CurrentCount => semaphoreSlim.CurrentCount;
  public int CurrentQueue => 0;
}