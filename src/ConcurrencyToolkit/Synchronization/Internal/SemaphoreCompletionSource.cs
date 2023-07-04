// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Metrics;
using ConcurrencyToolkit.Pooling.LiteObjectPool;
using ConcurrencyToolkit.Tasks.Sources;

namespace ConcurrencyToolkit.Synchronization.Internal;

/// <summary>
/// An async completable which provides ValueTask and ValueTask{bool} with always true result.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal class SemaphoreCompletionSource
  : IValueTaskSource,
    IValueTaskSource<bool>
{
  private ManualResetCompletionSourceCore<bool> core;

  public SemaphoreCompletionSource()
  {
    Counter<CompletionAllocated>.Increment();
  }

  public static SemaphoreCompletionSource Get(bool async)
  {
    var obj = LiteObjectPool<SemaphoreCompletionSource>.TryRent();
    if (obj == null) obj = new();
    else Counter<CompletionReused>.Increment();

    if (obj.core.RunContinuationsAsynchronously != async)
      obj.core.RunContinuationsAsynchronously = async;
    return obj;
  }

  public void SetRunContinuationsAsynchronously(bool async)
  {
    if (core.RunContinuationsAsynchronously != async)
      core.RunContinuationsAsynchronously = async;
  }

  public void ResetAndReturn(bool result = false)
  {
    core.Reset();
    if (this is CancellableSemaphoreCompletionSource cscs)
    {
      cscs.DisposeRegistration(result);
      LiteObjectPool<CancellableSemaphoreCompletionSource>.Return(cscs);
    }
    else
    {
      LiteObjectPool<SemaphoreCompletionSource>.Return(this);
    }
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void HandleNonSuccess() => ThrowHelper.InvalidOperation();

  void IValueTaskSource.GetResult(short token)
  {
    bool result = false;
    try
    {
      if (!(result = core.GetResult(token)))
        HandleNonSuccess();
    }
    finally
    {
      ResetAndReturn(result);
    }
  }

  bool IValueTaskSource<bool>.GetResult(short token)
  {
    bool result = false;
    try
    {
      return result = core.GetResult(token);
    }
    finally
    {
      ResetAndReturn(result);
    }
  }

  public ValueTaskSourceStatus GetStatus(short token) => core.GetStatus(token);

  // [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    => core.OnCompleted(continuation, state, token, flags);

  public ValueTask ValueTask => new(this, core.Version);
  public ValueTask<bool> ValueTaskBool => new(this, core.Version);

  public void SetResult(bool result) => core.SetResult(result);

  public void Wait()
  {
    var version = core.Version;
    core.WaitSynchronously(version);
    ((IValueTaskSource)this).GetResult(version);
  }

  public bool TryWait()
  {
    var version = core.Version;
    core.WaitSynchronously(version);
    return ((IValueTaskSource<bool>)this).GetResult(version);
  }
}