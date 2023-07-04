// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;

namespace ConcurrencyToolkit.Internal;

[EventSource(Guid = "082CC2CA-4C3F-4DF4-972A-6196A24EFAF4", Name = "ConcurrencyToolkit")]
internal sealed class ConcurrencyToolkitEventSource : EventSource
{
  private enum Keywords : long
  {
    ArrayPool = 0x1
  }

  private const string EventSourceSuppressMessage = "Parameters to this method are primitive and are trimmer safe";
  internal static readonly ConcurrencyToolkitEventSource Log = new ConcurrencyToolkitEventSource();

  /// <summary>Bucket ID used when renting/returning an array that's too large for a pool.</summary>
  internal const int NoBucketId = -1;

  /// <summary>The reason for a BufferAllocated event.</summary>
  internal enum BufferAllocatedReason : int
  {
    /// <summary>The pool is allocating a buffer to be pooled in a bucket.</summary>
    Pooled,

    /// <summary>The requested buffer size was too large to be pooled.</summary>
    OverMaximumSize,

    /// <summary>The pool has already allocated for pooling as many buffers of a particular size as it's allowed.</summary>
    PoolExhausted
  }

  /// <summary>The reason for a BufferDropped event.</summary>
  internal enum BufferDroppedReason : int
  {
    /// <summary>The pool is full for buffers of the specified size.</summary>
    Full,

    /// <summary>The buffer size was too large to be pooled.</summary>
    OverMaximumSize,
  }

  private ConcurrencyToolkitEventSource()
  {
  }

  /// <summary>
  /// Event for when a buffer is rented.  This is invoked once for every successful call to Rent,
  /// regardless of whether a buffer is allocated or a buffer is taken from the pool.  In a
  /// perfect situation where all rented buffers are returned, we expect to see the number
  /// of BufferRented events exactly match the number of BuferReturned events, with the number
  /// of BufferAllocated events being less than or equal to those numbers (ideally significantly
  /// less than).
  /// </summary>
  [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
    Justification = EventSourceSuppressMessage)]
  [Event(1, Level = EventLevel.Verbose, Keywords = (EventKeywords)Keywords.ArrayPool)]
  internal unsafe void BufferRented(int bufferId, int bufferSize, int poolId, int bucketId)
  {
    EventData* payload = stackalloc EventData[4];
    payload[0].Size = sizeof(int);
    payload[0].DataPointer = (IntPtr)(&bufferId);
    payload[1].Size = sizeof(int);
    payload[1].DataPointer = (IntPtr)(&bufferSize);
    payload[2].Size = sizeof(int);
    payload[2].DataPointer = (IntPtr)(&poolId);
    payload[3].Size = sizeof(int);
    payload[3].DataPointer = (IntPtr)(&bucketId);
    WriteEventCore(1, 4, payload);
  }

  /// <summary>
  /// Event for when a buffer is allocated by the pool.  In an ideal situation, the number
  /// of BufferAllocated events is significantly smaller than the number of BufferRented and
  /// BufferReturned events.
  /// </summary>
  [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:UnrecognizedReflectionPattern",
    Justification = EventSourceSuppressMessage)]
  [Event(2, Level = EventLevel.Informational, Keywords = (EventKeywords)Keywords.ArrayPool)]
  internal unsafe void BufferAllocated(int bufferId, int bufferSize, int poolId, int bucketId,
    BufferAllocatedReason reason)
  {
    EventData* payload = stackalloc EventData[5];
    payload[0].Size = sizeof(int);
    payload[0].DataPointer = (IntPtr)(&bufferId);
    payload[1].Size = sizeof(int);
    payload[1].DataPointer = (IntPtr)(&bufferSize);
    payload[2].Size = sizeof(int);
    payload[2].DataPointer = (IntPtr)(&poolId);
    payload[3].Size = sizeof(int);
    payload[3].DataPointer = (IntPtr)(&bucketId);
    payload[4].Size = sizeof(BufferAllocatedReason);
    payload[4].DataPointer = (IntPtr)(&reason);
    WriteEventCore(2, 5, payload);
  }

  /// <summary>
  /// Event raised when a buffer is returned to the pool.  This event is raised regardless of whether
  /// the returned buffer is stored or dropped.  In an ideal situation, the number of BufferReturned
  /// events exactly matches the number of BufferRented events.
  /// </summary>
  [Event(3, Level = EventLevel.Verbose, Keywords = (EventKeywords)Keywords.ArrayPool)]
  internal void BufferReturned(int bufferId, int bufferSize, int poolId) => WriteEvent(3, bufferId, bufferSize, poolId);

  /// <summary>
  /// Event raised when we attempt to free a buffer due to inactivity or memory pressure (by no longer
  /// referencing it). It is possible (although not commmon) this buffer could be rented as we attempt
  /// to free it. A rent event before or after this event for the same ID, is a rare, but expected case.
  /// </summary>
  [Event(4, Level = EventLevel.Informational, Keywords = (EventKeywords)Keywords.ArrayPool)]
  internal void BufferTrimmed(int bufferId, int bufferSize, int poolId) => WriteEvent(4, bufferId, bufferSize, poolId);

  /// <summary>
  /// Event raised when we check to trim buffers.
  /// </summary>
  [Event(5, Level = EventLevel.Informational, Keywords = (EventKeywords)Keywords.ArrayPool)]
  internal void BufferTrimPoll(int milliseconds, int pressure) => WriteEvent(5, milliseconds, pressure);

  /// <summary>
  /// Event raised when a buffer returned to the pool is dropped.
  /// </summary>
  [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
    Justification = EventSourceSuppressMessage)]
  [Event(6, Level = EventLevel.Informational, Keywords = (EventKeywords)Keywords.ArrayPool)]
  internal unsafe void BufferDropped(int bufferId, int bufferSize, int poolId, int bucketId, BufferDroppedReason reason)
  {
    EventData* payload = stackalloc EventData[5];
    payload[0].Size = sizeof(int);
    payload[0].DataPointer = (IntPtr)(&bufferId);
    payload[1].Size = sizeof(int);
    payload[1].DataPointer = (IntPtr)(&bufferSize);
    payload[2].Size = sizeof(int);
    payload[2].DataPointer = (IntPtr)(&poolId);
    payload[3].Size = sizeof(int);
    payload[3].DataPointer = (IntPtr)(&bucketId);
    payload[4].Size = sizeof(BufferDroppedReason);
    payload[4].DataPointer = (IntPtr)(&reason);
    WriteEventCore(6, 5, payload);
  }
}