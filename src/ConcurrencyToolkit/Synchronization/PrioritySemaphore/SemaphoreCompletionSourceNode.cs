// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Sources;
using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Metrics;
using ConcurrencyToolkit.Pooling.LiteObjectPool;
using ConcurrencyToolkit.Synchronization.Internal;
using ConcurrencyToolkit.Tasks.Sources;

namespace ConcurrencyToolkit.Synchronization;

[StructLayout(LayoutKind.Auto)]
internal class SemaphoreCompletionSourceNode
  : IValueTaskSource,
    IValueTaskSource<bool>
{
  private ManualResetCompletionSourceCore<bool> core;
  public SemaphoreCompletionSourceNode? Prev;
  public SemaphoreCompletionSourceNode? Next;
  public PrioritySemaphore Semaphore;
  public CancellationTokenRegistration Registration;
  public int Priority;

  public SemaphoreCompletionSourceNode()
  {
    Counter<CompletionAllocated>.Increment();
  }

  public static SemaphoreCompletionSourceNode Get(bool async)
  {
    var obj = LiteObjectPool<SemaphoreCompletionSourceNode>.TryRent();
    if (obj == null) obj = new();
    else Counter<CompletionReused>.Increment();

    obj.SetRunContinuationsAsynchronously(async);
    return obj;
  }

  public void SetRunContinuationsAsynchronously(bool async)
  {
    if (core.RunContinuationsAsynchronously != async)
      core.RunContinuationsAsynchronously = async;
  }

  public void ResetAndReturn(bool result = false)
  {
    var registration = Registration;
    if (registration != default)
    {
      if (result)
      {
        Counter<RegistrationDisposed>.Increment();
        registration.Dispose();
      }
      Registration = default;
      Token = default;
    }
    core.Reset();
    LiteObjectPool<SemaphoreCompletionSourceNode>.Return(this);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected virtual void HandleNonSuccess()
  {
    Token.ThrowIfCancellationRequested();
    ThrowHelper.InvalidOperation();
  }

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

  public void Cancel(CancellationToken token)
  {
    Token = token;
    SetResult(false);
  }

  public CancellationToken Token { get; set; }

  public void Resume()
  {
    SetResult(true);
  }
}