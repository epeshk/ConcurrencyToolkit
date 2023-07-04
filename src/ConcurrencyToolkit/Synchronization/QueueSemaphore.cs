// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Synchronization.Internal.ProducerConsumerSemaphore;

namespace ConcurrencyToolkit.Synchronization;

using Impl = ProducerConsumerCollectionSemaphore<ConcurrentQueueWrapper<ValueHolder>>;

/// <summary>
/// The <see cref="ISemaphore"/> implementation which uses <see cref="ConcurrentQueue{T}"/> to order queued waiters.
/// </summary>
/// <remarks>
/// This implementation performs best when cancellation token is not used. Fast and almost no allocations.
/// <br/>
/// When cancellation token is provided, a new object will be allocated for each waiter.
/// </remarks>
[DebuggerDisplay("Count = {CurrentCount}, Queue = {CurrentQueue}")]
public sealed class QueueSemaphore : ISemaphore
{
  private readonly Impl semaphore;

  public QueueSemaphore(int initialCount) => semaphore = new(initialCount, new(new()));

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ValueTask AcquireAsync(CancellationToken cancellationToken = default) =>
    semaphore.AcquireAsync(cancellationToken);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public ValueTask<bool> TryAcquireAsync(CancellationToken cancellationToken = default) =>
    semaphore.TryAcquireAsync(cancellationToken);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Acquire(CancellationToken cancellationToken = default) =>
    semaphore.Acquire(cancellationToken);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryAcquire(CancellationToken cancellationToken = default) =>
    semaphore.TryAcquire(cancellationToken);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryAcquireImmediately() =>
    semaphore.TryAcquireImmediately();

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void Release() => semaphore.Release();

  public int CurrentCount => semaphore.CurrentCount;
  public int CurrentQueue => semaphore.CurrentQueue;
}