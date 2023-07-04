// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Collections.Queues;

namespace ConcurrencyToolkit.Pooling;

/// <summary>
/// Provides an IObjectPool implementation.
/// </summary>
/// <remarks>
/// The implementation uses a tiered caching scheme, with a small per-thread cache for single object, followed
/// by a cache shared by all threads, split into per-core queues meant to be used by threads
/// running on that core. <see cref="UnpaddedFixedSizeConcurrentQueue{T}"/> used as thread-safe queue implementation.
/// </remarks>
public sealed class LocklessObjectPool<T> : IObjectPool<T> where T : class
{
  private readonly Func<T> factory;
  private readonly Action<T>? cleanup;

  /// <summary>Maximum number of per-core stacks to use per array size.</summary>
  private const int MaxPerCorePerArraySizeQueues = 64; // selected to avoid needing to worry about processor groups

  /// <summary>The maximum number of buffers to store in a bucket's global queue.</summary>
  private const int MaxObjectsPerCore = 8;

  /// <summary>A per-thread storage for one object.</summary>
  private readonly ThreadLocal<T?> _tls = new (() => null);

  /// <summary>
  /// An object holding per-core object queues.
  /// </summary>
  private readonly PerCoreQueues _buckets;

  /// <summary>
  /// Creates an instance of <see cref="LocklessObjectPool{T}"/>.
  /// </summary>
  /// <param name="factory">A delegate that creates a new object of type <typeparamref name="T"/>.</param>
  /// <param name="cleanup">A delegate that is invoked on an object before it is returned to the pool. May be null.</param>
  /// <param name="concurrencyLevel">A number of concurrent queues that hold the pooled objects. A higher concurrency level reduces a synchronization overhead when accessing the pool.</param>
  /// <param name="objectsPerConcurrencyLevel">The number of objects that each queue can hold. Rounded up to the power of two.</param>
  /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="concurrencyLevel"/> or <paramref name="objectsPerConcurrencyLevel"/> is negative.</exception>
  public LocklessObjectPool(Func<T> factory, Action<T>? cleanup=null, int? concurrencyLevel=null, int? objectsPerConcurrencyLevel=null)
  {
    ArgumentNullException.ThrowIfNull(factory);
    if (concurrencyLevel is < 0)
      throw new ArgumentOutOfRangeException(nameof(concurrencyLevel), concurrencyLevel, "Value should not be negative");
    if (objectsPerConcurrencyLevel is < 0)
      throw new ArgumentOutOfRangeException(nameof(objectsPerConcurrencyLevel), objectsPerConcurrencyLevel, "Value should not be negative");

    this.factory = factory;
    this.cleanup = cleanup;

    _buckets = new(
      concurrencyLevel ?? Math.Min(MaxPerCorePerArraySizeQueues, Environment.ProcessorCount),
      (int)BitOperations.RoundUpToPowerOf2((uint)(objectsPerConcurrencyLevel ?? 8)));
  }

  /// <summary>Gets an ID for the pool to use with events.</summary>
  private int Id => GetHashCode();

  public T Rent()
  {
    T? obj;

    // First, try to get an object from TLS if possible.
    var tls = _tls;
    if ((obj = tls.Value) is not null)
    {
      tls.Value = null;
      return obj;
    }

    // Next, try to get an object from one of the per-core stacks.
    if ((obj = _buckets.TryPop()) is not null)
      return obj;

    obj = factory();
    ArgumentNullException.ThrowIfNull(obj);
    return obj;
  }

  public bool TryRent([NotNullWhen(true)] out T obj)
  {
    // First, try to get an object from TLS if possible.
    var tls = _tls;
    if ((obj = tls.Value) is not null)
    {
      tls.Value = null;
      return true;
    }

    // Next, try to get an object from one of the per-core stacks.
    if ((obj = _buckets.TryPop()) is not null)
      return true;

    return false;
  }

  public void Return(T obj)
  {
    ArgumentNullException.ThrowIfNull(obj);
    cleanup?.Invoke(obj);

    var tls = _tls;
    if (tls.Value is null)
    {
      tls.Value = obj;
      return;
    }

    _buckets.TryPush(obj);
  }

  /// <summary>Stores a set of stacks of arrays, with one stack per core.</summary>
  private sealed class PerCoreQueues
  {
    /// <summary>Number of queues to employ.</summary>
    private static readonly int s_queueCount = Math.Min(Environment.ProcessorCount, MaxPerCorePerArraySizeQueues);

    /// <summary>The stacks.</summary>
    private readonly UnpaddedFixedSizeConcurrentQueue<T>[] _perCoreQueues;

    /// <summary>Initializes the stacks.</summary>
    public PerCoreQueues(int queuesCount, int objectsPerQueue)
    {
      // Create the queues. We create as many as there are processors, limited by our max.
      var queues = new UnpaddedFixedSizeConcurrentQueue<T>[s_queueCount];
      for (int i = 0; i < queues.Length; i++)
      {
        queues[i] = new UnpaddedFixedSizeConcurrentQueue<T>(MaxObjectsPerCore);
      }

      _perCoreQueues = queues;
    }

    /// <summary>Try to push the array into the stacks. If each is full when it's tested, the array will be dropped.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryPush(T obj)
    {
      // Try to push on to the associated queue first.  If that fails,
      // round-robin through the other queues.
      var stacks = _perCoreQueues;
      int index = (int)((uint)Thread.GetCurrentProcessorId() % (uint)s_queueCount); // mod by constant in tier 1
      for (int i = 0; i < stacks.Length; i++)
      {
        if (stacks[index].TryEnqueue(obj)) return true;
        if (++index == stacks.Length) index = 0;
      }

      return false;
    }

    /// <summary>Try to get an array from the stacks.  If each is empty when it's tested, null will be returned.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? TryPop()
    {
      // Try to pop from the associated queue first.  If that fails, round-robin through the other queues.
      T? obj;
      var queues = _perCoreQueues;
      int index = (int)((uint)Thread.GetCurrentProcessorId() % (uint)s_queueCount); // mod by constant in tier 1
      for (int i = 0; i < queues.Length; i++)
      {
        if (queues[index].TryDequeue(out obj)) return obj;
        if (++index == queues.Length) index = 0;
      }

      return null;
    }
  }
}