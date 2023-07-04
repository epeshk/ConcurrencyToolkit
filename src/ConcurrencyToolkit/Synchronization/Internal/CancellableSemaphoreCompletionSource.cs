// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Metrics;
using ConcurrencyToolkit.Pooling.LiteObjectPool;

namespace ConcurrencyToolkit.Synchronization.Internal;

[StructLayout(LayoutKind.Auto)]
internal class CancellableSemaphoreCompletionSource
  : SemaphoreCompletionSource
{
  public CancellationTokenRegistration Registration;

  public CancellableSemaphoreCompletionSource() => Counter<CompletionAllocated>.Increment();

  public new static CancellableSemaphoreCompletionSource Get(bool async)
  {
    var obj = LiteObjectPool<CancellableSemaphoreCompletionSource>.TryRent();
    if (obj == null) obj = new();
    else Counter<CompletionReused>.Increment();

    obj.SetRunContinuationsAsynchronously(async);
    return obj;
  }

  public void DisposeRegistration(bool result)
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
    }

    Segment = default!;
    Index = default;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  protected override void HandleNonSuccess()
  {
    Token.ThrowIfCancellationRequested();
    ThrowHelper.Unreachable();
  }

  public object? Segment { get; set; }
  public byte Index { get; set; }

  public void SetCancelled(CancellationToken cancellationToken)
  {
    Token = cancellationToken;
    SetResult(false);
  }

  private CancellationToken Token;

#if WAITER_ID
  private ulong waiterId;
#endif

  [Conditional("WAITER_ID")]
  public void SetWaiterId(ulong id)
  {
#if WAITER_ID
  waiterId = id;
#endif
  }

  [Conditional("WAITER_ID")]
  public void AssertWaiterId(ulong id)
  {
#if WAITER_ID
  if (waiterId != id) ThrowHelper.Assertion($"Expected waiterId to be {id}, but: {waiterId}");
#endif
  }
}
