// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Metrics;

namespace ConcurrencyToolkit.Synchronization.Internal.ProducerConsumerSemaphore;

internal readonly record struct ValueHolder(object Value);

[DebuggerDisplay("Count = {CurrentCount}, Queue = {CurrentQueue}")]
internal sealed class ProducerConsumerCollectionSemaphore<TCollection> : ISemaphore
  where TCollection : IProducerConsumerCollection<ValueHolder>
{
  private struct CancellationData
  {
    public ProducerConsumerCollectionSemaphore<TCollection> Semaphore;
    public CancellationTokenRegistration Registration;
  }

  private volatile int currentCount;
  private volatile int failedCount;
  private volatile int canceledCount;
  private readonly TCollection collection;

  public ProducerConsumerCollectionSemaphore(int initialCount, TCollection collection)
  {
    if (initialCount < 0)
      throw new ArgumentOutOfRangeException(nameof(initialCount), $"Initial count must not be negative, but was '{initialCount}'.");

    currentCount = initialCount;
    this.collection = collection;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryAcquireImmediately()
  {
    while (true)
    {
      var current = currentCount;
      if (current < 1) return false;
      if (Interlocked.CompareExchange(ref currentCount, current - 1, current) == current)
        return true;
    }
  }

  public int CurrentCount => Math.Max(0, currentCount);
  public int CurrentQueue => Math.Max(0, Math.Max(0, -currentCount) - canceledCount);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ValueTask AcquireAsync(CancellationToken cancellationToken=default)
  {
    if (TryEnter())
      return ValueTask.CompletedTask;

    return !cancellationToken.CanBeCanceled
      ? NonCancellable()
      : Cancellable(cancellationToken);

    ValueTask NonCancellable() => CreateWaiter(true)?.ValueTask ?? ValueTasks.Canceled;

    [MethodImpl(MethodImplOptions.NoInlining)]
    ValueTask Cancellable(CancellationToken token) => new(CreateCancellableWaiter(token, true, false).Task);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ValueTask<bool> TryAcquireAsync(CancellationToken cancellationToken=default)
  {
    if (TryEnter())
      return ValueTask.FromResult(true);

    return !cancellationToken.CanBeCanceled
      ? NonCancellable()
      : Cancellable(cancellationToken);

    ValueTask<bool> NonCancellable() => CreateWaiter(true)?.ValueTaskBool ?? ValueTask.FromResult(false);

    [MethodImpl(MethodImplOptions.NoInlining)]
    ValueTask<bool> Cancellable(CancellationToken token) => new(CreateCancellableWaiter(token, true, true).Task);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Acquire(CancellationToken cancellationToken=default)
  {
    if (TryEnter())
      return;
    if (!cancellationToken.CanBeCanceled)
    {
      NonCancellable();
      return;
    }

    Cancellable(cancellationToken);
    return;

    void NonCancellable()
    {
      var waiter = CreateWaiter(false);
      if (waiter is null) ThrowHelper.OperationCanceledWithoutToken();
      waiter.Wait();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void Cancellable(CancellationToken token) => CreateCancellableWaiter(token, false, false).Task.WaitSynchronously();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryAcquire(CancellationToken cancellationToken=default)
  {
    if (TryEnter())
      return true;

    return !cancellationToken.CanBeCanceled
      ? NonCancellable()
      : Cancellable(cancellationToken);

    bool NonCancellable() => CreateWaiter(false)?.TryWait() ?? false;

    [MethodImpl(MethodImplOptions.NoInlining)]
    bool Cancellable(CancellationToken token) => CreateCancellableWaiter(token, false, true).Task.WaitSynchronously();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool TryEnter() => Interlocked.Decrement(ref currentCount) >= 0;

  private SemaphoreCompletionSource? CreateWaiter(bool async)
  {
    try
    {
      var waiter = SemaphoreCompletionSource.Get(async);
      if (collection.TryAdd(new(waiter))) return waiter;
      Interlocked.Increment(ref failedCount);
      return null;
    }
    catch
    {
      Interlocked.Increment(ref failedCount);
      throw;
    }
  }

  private TaskWaiter<CancellationData> CreateCancellableWaiter(
    CancellationToken token,
    bool async,
    bool silentCancellation)
  {
    try
    {
      var waiter = new TaskWaiter<CancellationData>(async, silentCancellation);
      ref var closure = ref waiter.Closure;
      closure.Semaphore = this;
      closure.Registration = token.UnsafeRegister(static (o, token) =>
      {
        var tw = (TaskWaiter<CancellationData>)o!;
        if (!tw.TryCancel(token))
          return;
        Interlocked.Increment(ref tw.Closure.Semaphore.canceledCount);
      }, waiter);

      if (collection.TryAdd(new(waiter)))
        return waiter;

      Interlocked.Increment(ref failedCount);
      return TaskWaiter<CancellationData>.Canceled;
    }
    catch
    {
      Interlocked.Increment(ref failedCount);
      throw;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Release()
  {
    var countBeforeRelease = Interlocked.Increment(ref currentCount) - 1;
    if (countBeforeRelease < 0 && !SignalWaiter())
      ReleaseLoop();
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private void ReleaseLoop()
  {
    Counter<ReleaseLoopEntered>.Increment();
    while (true)
    {
      var countBeforeRelease = Interlocked.Increment(ref currentCount) - 1;
      if (countBeforeRelease >= 0 || SignalWaiter())
        return;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool SignalWaiter()
  {
    if (collection.TryTake(out var waiter))
    {
      return CompleteWaiter(waiter.Value);
    }

    return SignalDeferredWaiter();
  }

  private bool CompleteWaiter(object waiter)
  {
    if (waiter is SemaphoreCompletionSource scs)
    {
      scs.SetResult(true);
      return true;
    }

    return CompleteTaskWaiter(waiter);
  }

  private bool CompleteTaskWaiter(object waiter)
  {
    var tw = (TaskWaiter<CancellationData>)waiter;
    if (!tw.TryResume())
    {
      Interlocked.Decrement(ref canceledCount);
      return false;
    }

    tw.Closure.Registration.Dispose();
    return true;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool SignalDeferredWaiter()
  {
    while (true)
    {
      var spinner = new SpinWait();

      while (true)
      {
        if (EliminateFailedAcquire())
          return false;
        if (collection.TryTake(out var waiter))
          return CompleteWaiter(waiter.Value);

        spinner.SpinOnce(-1);
      }
    }
  }

  private bool EliminateFailedAcquire()
  {
    var curFailedCount = Volatile.Read(ref failedCount);
    if (curFailedCount <= 0) return false;
    return Interlocked.CompareExchange(ref failedCount, curFailedCount - 1, curFailedCount) == curFailedCount;
  }
}