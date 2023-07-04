// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ConcurrencyToolkit.Collections.Queues;

/// <summary>
/// Provides a multi-producer, multi-consumer thread-safe bounded segment.  When the queue is full,
/// enqueues fail and return false.  When the queue is empty, dequeues fail and return null.
/// </summary>
[DebuggerDisplay("Capacity = {Capacity}")]
public sealed class FixedSizeConcurrentQueue<T> : IProducerConsumerCollection<T>
{
  // Segment design is inspired by the algorithm outlined at:
  // http://www.1024cores.net/home/lock-free-algorithms/queues/bounded-mpmc-queue

  /// <summary>The array of items in this queue.  Each slot contains the item in that slot and its "sequence number".</summary>
  private readonly Slot[] _slots;

  /// <summary>Mask for quickly accessing a position within the queue's array.</summary>
  private readonly int _slotsMask;

  /// <summary>The head and tail positions, with padding to help avoid false sharing contention.</summary>
  /// <remarks>Dequeuing happens from the head, enqueuing happens at the tail.</remarks>
  private PaddedHeadAndTail _headAndTail; // mutable struct: do not make this readonly

  /// <summary>Creates the bounded concurrent queue.</summary>
  /// <param name="boundedLength">
  /// The maximum number of elements the segment can contain.  Must be a power of 2.
  /// </param>
  public FixedSizeConcurrentQueue(int boundedLength)
  {
    // Validate the length
    if (boundedLength < 2)
      throw new ArgumentOutOfRangeException(nameof(boundedLength), boundedLength, $"Must be a power of 2, got {boundedLength}");
    if ((boundedLength & (boundedLength - 1)) != 0)
      throw new ArgumentOutOfRangeException(nameof(boundedLength), boundedLength, $"Must be a power of 2, got {boundedLength}");

    // Initialize the slots and the mask.  The mask is used as a way of quickly doing "% _slots.Length",
    // instead letting us do "& _slotsMask".
    _slots = new Slot[boundedLength];
    _slotsMask = boundedLength - 1;

    // Initialize the sequence number for each slot.  The sequence number provides a ticket that
    // allows dequeuers to know whether they can dequeue and enqueuers to know whether they can
    // enqueue.  An enqueuer at position N can enqueue when the sequence number is N, and a dequeuer
    // for position N can dequeue when the sequence number is N + 1.  When an enqueuer is done writing
    // at position N, it sets the sequence number to N + 1 so that a dequeuer will be able to dequeue,
    // and when a dequeuer is done dequeueing at position N, it sets the sequence number to N + _slots.Length,
    // so that when an enqueuer loops around the slots, it'll find that the sequence number at
    // position N is N.  This also means that when an enqueuer finds that at position N the sequence
    // number is < N, there is still a value in that slot, i.e. the segment is full, and when a
    // dequeuer finds that the value in a slot is < N + 1, there is nothing currently available to
    // dequeue. (It is possible for multiple enqueuers to enqueue concurrently, writing into
    // subsequent slots, and to have the first enqueuer take longer, so that the slots for 1, 2, 3, etc.
    // may have values, but the 0th slot may still be being filled... in that case, TryDequeue will
    // return false.)
    for (int i = 0; i < _slots.Length; i++)
    {
      _slots[i].SequenceNumber = i;
    }
  }

  /// <summary>Gets the number of elements this segment can store.</summary>
  internal int Capacity => _slots.Length;

  /// <summary>Tries to dequeue an element from the queue.</summary>
  public bool TryDequeue(out T? item)
  {
    Slot[] slots = _slots;

    // Loop in case of contention...
    SpinWait spinner = default;
    while (true)
    {
      // Get the head at which to try to dequeue.
      int currentHead = Volatile.Read(ref _headAndTail.Head);
      int slotsIndex = currentHead & _slotsMask;

      // Read the sequence number for the head position.
      int sequenceNumber = Volatile.Read(ref slots[slotsIndex].SequenceNumber);

      // We can dequeue from this slot if it's been filled by an enqueuer, which
      // would have left the sequence number at pos+1.
      int diff = sequenceNumber - (currentHead + 1);
      if (diff == 0)
      {
        // We may be racing with other dequeuers.  Try to reserve the slot by incrementing
        // the head.  Once we've done that, no one else will be able to read from this slot,
        // and no enqueuer will be able to read from this slot until we've written the new
        // sequence number. WARNING: The next few lines are not reliable on a runtime that
        // supports thread aborts. If a thread abort were to sneak in after the CompareExchange
        // but before the Volatile.Write, enqueuers trying to enqueue into this slot would
        // spin indefinitely.  If this implementation is ever used on such a platform, this
        // if block should be wrapped in a finally / prepared region.
        if (Interlocked.CompareExchange(ref _headAndTail.Head, currentHead + 1, currentHead) ==
            currentHead)
        {
          // Successfully reserved the slot.  Note that after the above CompareExchange, other threads
          // trying to dequeue from this slot will end up spinning until we do the subsequent Write.
          item = slots[slotsIndex].Item!;

          // If we're preserving, though, we don't zero out the slot, as we need it for
          // enumerations, peeking, ToArray, etc.  And we don't update the sequence number,
          // so that an enqueuer will see it as full and be forced to move to a new segment.
          if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
          {
            slots[slotsIndex].Item = default;
          }

          Volatile.Write(ref slots[slotsIndex].SequenceNumber, currentHead + slots.Length);

          return true;
        }

        // The head was already advanced by another thread. A newer head has already been observed and the next
        // iteration would make forward progress, so there's no need to spin-wait before trying again.
      }
      else if (diff < 0)
      {
        // The sequence number was less than what we needed, which means this slot doesn't
        // yet contain a value we can dequeue, i.e. the segment is empty.  Technically it's
        // possible that multiple enqueuers could have written concurrently, with those
        // getting later slots actually finishing first, so there could be elements after
        // this one that are available, but we need to dequeue in order.  So before declaring
        // failure and that the segment is empty, we check the tail to see if we're actually
        // empty or if we're just waiting for items in flight or after this one to become available.
        int currentTail = Volatile.Read(ref _headAndTail.Tail);
        if (currentTail - currentHead <= 0)
        {
          item = default;
          return false;
        }

        // It's possible it could have become frozen after we checked _frozenForEnqueues
        // and before reading the tail.  That's ok: in that rare race condition, we just
        // loop around again. This is not necessarily an always-forward-progressing
        // situation since this thread is waiting for another to write to the slot and
        // this thread may have to check the same slot multiple times. Spin-wait to avoid
        // a potential busy-wait, and then try again.
        spinner.SpinOnce(sleep1Threshold: -1);
      }
      else
      {
        // The item was already dequeued by another thread. The head has already been updated beyond what was
        // observed above, and the sequence number observed above as a volatile load is more recent than the update
        // to the head. So, the next iteration of the loop is guaranteed to see a new head. Since this is an
        // always-forward-progressing situation, there's no need to spin-wait before trying again.
      }
    }
  }

  /// <summary>
  /// Attempts to enqueue the item.  If successful, the item will be stored
  /// in the queue and true will be returned; otherwise, the item won't be stored, and false
  /// will be returned.
  /// </summary>
  public bool TryEnqueue(T item)
  {
    Slot[] slots = _slots;

    // Loop in case of contention...
    while (true)
    {
      // Get the tail at which to try to return.
      int currentTail = Volatile.Read(ref _headAndTail.Tail);
      int slotsIndex = currentTail & _slotsMask;

      // Read the sequence number for the tail position.
      int sequenceNumber = Volatile.Read(ref slots[slotsIndex].SequenceNumber);

      // The slot is empty and ready for us to enqueue into it if its sequence
      // number matches the slot.
      int diff = sequenceNumber - currentTail;
      if (diff == 0)
      {
        // We may be racing with other enqueuers.  Try to reserve the slot by incrementing
        // the tail.  Once we've done that, no one else will be able to write to this slot,
        // and no dequeuer will be able to read from this slot until we've written the new
        // sequence number. WARNING: The next few lines are not reliable on a runtime that
        // supports thread aborts. If a thread abort were to sneak in after the CompareExchange
        // but before the Volatile.Write, other threads will spin trying to access this slot.
        // If this implementation is ever used on such a platform, this if block should be
        // wrapped in a finally / prepared region.
        if (Interlocked.CompareExchange(ref _headAndTail.Tail, currentTail + 1, currentTail) == currentTail)
        {
          // Successfully reserved the slot.  Note that after the above CompareExchange, other threads
          // trying to return will end up spinning until we do the subsequent Write.
          slots[slotsIndex].Item = item;
          Volatile.Write(ref slots[slotsIndex].SequenceNumber, currentTail + 1);
          return true;
        }

        // The tail was already advanced by another thread. A newer tail has already been observed and the next
        // iteration would make forward progress, so there's no need to spin-wait before trying again.
      }
      else if (diff < 0)
      {
        // The sequence number was less than what we needed, which means this slot still
        // contains a value, i.e. the segment is full.  Technically it's possible that multiple
        // dequeuers could have read concurrently, with those getting later slots actually
        // finishing first, so there could be spaces after this one that are available, but
        // we need to enqueue in order.
        return false;
      }
      else
      {
        // Either the slot contains an item, or it is empty but because the slot was filled and dequeued. In either
        // case, the tail has already been updated beyond what was observed above, and the sequence number observed
        // above as a volatile load is more recent than the update to the tail. So, the next iteration of the loop
        // is guaranteed to see a new tail. Since this is an always-forward-progressing situation, there's no need
        // to spin-wait before trying again.
      }
    }
  }

  /// <summary>Represents a slot in the queue.</summary>
  [StructLayout(LayoutKind.Auto)]
  [DebuggerDisplay("Item = {Item}, SequenceNumber = {SequenceNumber}")]
  private struct Slot
  {
    /// <summary>The item.</summary>
    public T? Item;

    /// <summary>The sequence number for this slot, used to synchronize between enqueuers and dequeuers.</summary>
    public int SequenceNumber;
  }

  public int Count
  {
    get
    {
      var head = _headAndTail.Head;
      var tail = _headAndTail.Tail;

      if (head <= tail) return tail - head;
      return Capacity + tail - head;
    }
  }

  #region useless members

  public IEnumerator<T> GetEnumerator() => throw new NotSupportedException();
  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
  public void CopyTo(Array array, int index) => throw new NotSupportedException();
  public bool IsSynchronized => false;
  public object SyncRoot => throw new NotSupportedException();
  void IProducerConsumerCollection<T>.CopyTo(T[] array, int index) => throw new NotSupportedException();
  T[] IProducerConsumerCollection<T>.ToArray() => throw new NotSupportedException();
  #endregion

  bool IProducerConsumerCollection<T>.TryAdd(T item) => TryEnqueue(item);

  bool IProducerConsumerCollection<T>.TryTake(out T item) => TryDequeue(out item);
}

/// <summary>Padded head and tail indices, to avoid false sharing between producers and consumers.</summary>
[DebuggerDisplay("Head = {Head}, Tail = {Tail}")]
[StructLayout(LayoutKind.Explicit, Size = 384)] // padding before/between/after fields based on worst case cache line size of 128.
// Sadly, there is no way to vary the size across different architectures from library
internal struct PaddedHeadAndTail
{
  [FieldOffset(128)] public int Head;
  [FieldOffset(256)] public int Tail;
}