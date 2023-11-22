// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Pooling;

namespace ConcurrencyToolkit.Collections;

internal struct SingleWriterSegment<TKey, TValue, TComparer> : IEnumerable<KeyValuePair<TKey, TValue>>
  where TComparer : struct, IEqualityComparer<TKey>
  where TKey : notnull
{
  private const int DefaultCopyArraySize = 64;
  private const int BucketsPerVersion = 4;
  private const int HashCodesMask = int.MaxValue;

  public readonly object SyncObject = new();
  private TComparer comparer;

  private volatile State state;
  private volatile int count;
  private volatile int freeList;
  private volatile int freeCount;

  public int Count => count - freeCount;

  public int Capacity => state?.entries?.Length ?? 0;

  public SingleWriterSegment(int capacity, TComparer comparer)
  {
    this.comparer = comparer;
    count = 0;
    freeCount = 0;
    freeList = -1;

    if (capacity < 0)
      throw new ArgumentOutOfRangeException(nameof(capacity));

    state = new State(HashHelpers.GetPrime(Math.Max(3, capacity)));
  }

  #region Insert

  /// <summary>
  /// <para>Returns <c>true</c> if a new key/value pair was added.</para>
  /// <para>Returns <c>false</c> if <paramref name="canOverwrite"/> is <c>true</c> and an existing key/value pair was overwritten.</para>
  /// <para>Returns <c>false</c> if <paramref name="canOverwrite"/> is <c>false</c> and an existing key/value pair was encountered.</para>
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal bool Insert<TModifyPolicy>(TKey key, TValue value, uint hashCode) where TModifyPolicy : struct, IModifyPolicy
  {
    var state = this.state;
    var buckets = state.buckets;
    var entries = state.entries;

    var bucket = state.Index((uint)hashCode);
    ref var version = ref Unsafe.NullRef<int>();
    if (!Atomic<TValue>.IsValueWriteAtomic)
      version = ref state.bucketVersions[GetBucketVersionIndex(bucket)];

    var collisionCount = 0;
    for (var i = buckets[bucket]; i != -1; i = entries[i].next)
    {
      if (entries[i].hash == hashCode && comparer.Equals(entries[i].key, key))
      {
        if (TModifyPolicy.CanModify)
        {
          if (!Atomic<TValue>.IsValueWriteAtomic)
          {
            var cur = MarkBucketForWriting(ref version);
            entries[i].value = value;
            UnmarkBucketForWriting(ref version, cur);
          }
          else
          {
            entries[i].value = value;
          }

          return true;
        }

        return false;
      }

      if (++collisionCount > entries.Length)
        ThrowHelper.InvalidOperation_ConcurrentModification();
    }

    if (!TModifyPolicy.MayAdd)
      return false;

    return AddNewItem(key, value, hashCode, state, bucket, ref version);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal bool Update(TKey key, TValue value, TValue comparand, uint hashCode)
  {
    var state = this.state;
    var buckets = state.buckets;
    var entries = state.entries;

    var bucket = state.Index((uint)hashCode);
    ref var version = ref Unsafe.NullRef<int>();
    if (!Atomic<TValue>.IsValueWriteAtomic)
      version = ref state.bucketVersions[GetBucketVersionIndex(bucket)];

    var collisionCount = 0;
    for (var i = buckets[bucket]; i != -1; i = entries[i].next)
    {
      if (entries[i].hash == hashCode && comparer.Equals(entries[i].key, key))
      {
        if (!EqualityComparer<TValue>.Default.Equals(entries[i].value, comparand))
          return false;

        if (!Atomic<TValue>.IsValueWriteAtomic)
        {
          var cur = MarkBucketForWriting(ref version);
          entries[i].value = value;
          UnmarkBucketForWriting(ref version, cur);
        }
        else
        {
          entries[i].value = value;
        }

        return true;
      }

      if (++collisionCount > entries.Length)
        ThrowHelper.InvalidOperation_ConcurrentModification();
    }

    return false;
  }

  private bool AddNewItem(TKey key, TValue value, uint hashCode, State state, uint bucket, ref int version)
  {
    if (Atomic<TValue>.IsValueWriteAtomic)
      version = ref state.bucketVersions[GetBucketVersionIndex(bucket)];

    int index;
    if (freeCount > 0)
    {
      index = freeList;
      freeList = state.entries[index].next;
      freeCount--;
    }
    else if (count == state.entries.Length)
    {
      return InsertWithResize(key, value, hashCode);
    }
    else
    {
      index = count++;
    }

    state.entries[index].value = value;
    state.entries[index].key = key;
    state.entries[index].hash = hashCode;
    state.entries[index].next = state.buckets[bucket];
    var cur = MarkBucketForWriting(ref version);
    state.buckets[bucket] = index;
    UnmarkBucketForWriting(ref version, cur);

    return true;
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool InsertWithResize(TKey key, TValue value, uint hashCode)
  {
    Resize();

    var state = this.state;
    var entries = state.entries;
    var buckets = state.buckets;

    var bucket = state.Index((uint)hashCode);
    var bucketVersionIndex = GetBucketVersionIndex(bucket);
    var index = count++;

    entries[index].value = value;
    entries[index].key = key;
    entries[index].hash = hashCode;
    entries[index].next = state.buckets[bucket];

    var cur = MarkBucketForWriting(state, bucketVersionIndex);
    buckets[bucket] = index;
    UnmarkBucketForWriting(state, bucketVersionIndex, cur);

    return true;
  }

  /// <summary>
  /// <para>This method exists to optimize resizing in <see cref="StripedGuidDictionary{T}"/>.</para>
  /// <para>It omits some of the code related to concurrency with readers and free list management.</para>
  /// <para>Caution: calls to this method must not contend with any read access!</para>
  /// <para>Caution: calls to this method must only be made with unique keys!</para>
  /// <para>Caution: calls to this method must only be made before any removals!</para>
  /// </summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal void AddUnsafe(TKey key, TValue value, uint hashCode)
  {
    var bucket = state.Index((uint)hashCode);

    if (count == state.entries.Length)
    {
      Resize();
      bucket = state.Index((uint)hashCode);
    }

    var index = count++;

    state.entries[index].value = value;
    state.entries[index].key = key;
    state.entries[index].hash = hashCode;
    state.entries[index].next = state.buckets[bucket];
    state.buckets[bucket] = index;
  }

  #endregion

  #region Remove

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool Remove(TKey key, uint hashCode, out TValue value)
  {
    var state = this.state;
    var buckets = state.buckets;
    var entries = state.entries;

    var bucket = state.Index((uint)hashCode);
    var bucketVersionIndex = GetBucketVersionIndex(bucket);
    var last = -1;

    for (var i = buckets[bucket]; i != -1; last = i, i = entries[i].next)
    {
      if (entries[i].hash == hashCode && comparer.Equals(entries[i].key, key))
      {
        value = entries[i].value;
        var cur = MarkBucketForWriting(state, bucketVersionIndex);

        if (last < 0)
        {
          buckets[bucket] = entries[i].next;
        }
        else
        {
          entries[last].next = entries[i].next;
        }

        entries[i].value = default;
        entries[i].key = default;
        entries[i].hash = uint.MaxValue;
        entries[i].next = freeList;

        UnmarkBucketForWriting(state, bucketVersionIndex, cur);

        freeList = i;
        freeCount++;

        return true;
      }
    }

    value = default;
    return false;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool Remove(KeyValuePair<TKey, TValue> pair, uint hashCode)
  {
    var state = this.state;
    var buckets = state.buckets;
    var entries = state.entries;

    var bucket = state.Index((uint)hashCode);
    var bucketVersionIndex = GetBucketVersionIndex(bucket);
    var last = -1;
    var collisionCount = 0u;

    for (var i = buckets[bucket]; i != -1; last = i, i = entries[i].next)
    {
      if (entries[i].hash == hashCode && comparer.Equals(entries[i].key, pair.Key))
      {
        if (!EqualityComparer<TValue>.Default.Equals(entries[i].value, pair.Value))
          return false;

        var cur = MarkBucketForWriting(state, bucketVersionIndex);

        if (last < 0)
        {
          buckets[bucket] = entries[i].next;
        }
        else
        {
          entries[last].next = entries[i].next;
        }

        entries[i].value = default;
        entries[i].key = default;
        entries[i].hash = uint.MaxValue;
        entries[i].next = freeList;

        UnmarkBucketForWriting(state, bucketVersionIndex, cur);

        freeList = i;
        freeCount++;

        return true;
      }

      if (++collisionCount > (uint) state.entries.Length)
        ThrowHelper.InvalidOperation_ConcurrentModification();
    }

    return false;
  }

  #endregion

  #region TryGetValue

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryGetValue(TKey key, uint hashCode, out TValue value)
  {
    var currentState = state;

    var bucket = (int)currentState.Index((uint)hashCode);

    if (TrySearchInBucket(currentState, key, bucket, hashCode, out var found, out value))
      return found;

    return TryGetValue_SlowPath(key, hashCode, out value, currentState, bucket);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public bool TryGetValueUnsafe(TKey key, uint hashCode, out TValue value)
  {
    var currentState = state;

    var bucket = (int)currentState.Index((uint)hashCode);

    return SearchInBucketUnsafe(currentState, key, bucket, hashCode, out value);
  }

  [MethodImpl(MethodImplOptions.NoInlining)]
  private bool TryGetValue_SlowPath(TKey key, uint hashCode, out TValue value, State currentState, int bucket)
  {
    bool found;
    var spinner = new SpinWait();

    do
    {
      spinner.SpinOnce(-1);
    } while (!TrySearchInBucket(currentState, key, bucket, hashCode, out found, out value));

    return found;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool TrySearchInBucket(State state, TKey key, int bucket, uint hashCode, out bool found, out TValue value)
  {
    found = false;
    value = default;

    var bucketVersionIndex = GetBucketVersionIndex(bucket);
    ref var version = ref state.bucketVersions[bucketVersionIndex];

    if (IsMarkedForWriting(ref version, out var versionBefore))
      return false;

    var collisionCount = 0u;

    Entry entry;
    for (var index = state.buckets[bucket]; index != -1; index = entry.next)
    {
      if (!TryReadEntryExpectingHash(state, index, ref version, versionBefore, out entry, hashCode, out var hashCodeEquals))
        return false;

      if (hashCodeEquals && comparer.Equals(entry.key, key))
      {
        found = true;

        // may be optimized by reading value to directly to `out value` inside `TryReadEntry`,
        // it is useful for large values, but this optimization is too unsafe, because it may expose value for other key
        value = entry.value;
        break;
      }

      if (++collisionCount > (uint) state.entries.Length)
        ThrowHelper.InvalidOperation_ConcurrentModification();
    }

    return true;//Volatile.Read(ref version) == versionBefore;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool SearchInBucketUnsafe(State state, TKey key, int bucket, uint hashCode, out TValue value)
  {
    value = default;

    var index = state.buckets[bucket];
    var collisionCount = 0u;

    while (index != -1)
    {
      ref var entry = ref state.entries[index];
      var hashCodeEquals = /*SkipHashCodeComparison || */entry.hash == hashCode;

      if (hashCodeEquals && comparer.Equals(entry.key, key))
      {
        value = entry.value;
        return true;
      }

      index = entry.next;

      if (++collisionCount > (uint) state.entries.Length)
        ThrowHelper.InvalidOperation_ConcurrentModification();
    }

    return false;
  }

  #endregion

  #region Capacity management

  private void Initialize(int capacity)
  {
    state = new State(HashHelpers.GetPrime(capacity));
  }

  private void Resize()
  {
    var newSize = HashHelpers.ExpandPrime(count);

    var newState = new State(newSize);

    var currentState = state;
    Array.Copy(currentState.entries, 0, newState.entries, 0, count);

    var newEntries = newState.entries;
    var newBuckets = newState.buckets;

    for (int i = 0; i < count; i++)
    {
      var hash = comparer.GetHashCode(newEntries[i].key) & HashCodesMask;
      var bucket = newState.Index((uint)hash);
      newEntries[i].next = newBuckets[bucket];
      newBuckets[bucket] = i;
    }

    Volatile.Write(ref state, newState);
  }

  #endregion

  #region Enumeration

  public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
  {
    var currentState = state;
    var array = LocklessArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(DefaultCopyArraySize);

    try
    {
      for (var bucket = 0; bucket < currentState.buckets.Length; bucket++)
      {
        if (!TryCopyBucket(currentState, bucket, array, out var copiedCount))
        {
          var spinner = new SpinWait();

          do
          {
            if (copiedCount == array.Length)
            {
              LocklessArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(array);

              array = LocklessArrayPool<KeyValuePair<TKey, TValue>>.Shared.Rent(array.Length * 2);
            }
            else
            {
              spinner.SpinOnce(-1);
            }
          }
          while (!TryCopyBucket(currentState, bucket, array, out copiedCount));
        }

        for (var i = 0; i < copiedCount; i++)
        {
          yield return array[i];
        }
      }
    }
    finally
    {
      LocklessArrayPool<KeyValuePair<TKey, TValue>>.Shared.Return(array);
    }
  }

  IEnumerator IEnumerable.GetEnumerator()
  {
    return GetEnumerator();
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static bool TryCopyBucket(State state, int bucket, KeyValuePair<TKey, TValue>[] array, out int copiedCount)
  {
    copiedCount = 0;

    var bucketVersionIndex = GetBucketVersionIndex(bucket);

    if (IsMarkedForWriting(ref state.bucketVersions[bucketVersionIndex], out var versionBefore))
      return false;

    var index = state.buckets[bucket];
    var collisionCount = 0u;

    while (index != -1)
    {
      if (!TryReadEntry(state, index, bucketVersionIndex, versionBefore, out var entry))
        return false;

      if (copiedCount == array.Length)
        return false;

      array[copiedCount++] = new KeyValuePair<TKey, TValue>(entry.key, entry.value);

      index = entry.next;

      if (++collisionCount > (uint) state.entries.Length)
        ThrowHelper.InvalidOperation_ConcurrentModification();
    }

    return Volatile.Read(ref state.bucketVersions[bucketVersionIndex]) == versionBefore;
  }

  #endregion

  #region Unsafe enumeration

  public IEnumerable<KeyValuePair<TKey, TValue>> EnumerateUnsafe()
  {
    var entries = this.state.entries;
    var ret = 0;
    var count = Count;
    for (var i = 0; i < entries.Length; ++i)
    {
      if (entries[i].hash == uint.MaxValue)
        continue;
      yield return new(entries[i].key, entries[i].value);
      if (++ret >= count)
        break;
    }
  }

  #endregion

  #region Helper methods

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int GetBucketVersionIndex(int bucket)
  {
    return bucket / BucketsPerVersion;
  }
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int GetBucketVersionIndex(uint bucket)
  {
    return (int)(bucket / BucketsPerVersion);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int MarkBucketForWriting(State state, int versionIndex) =>
    MarkBucketForWriting(ref state.bucketVersions[versionIndex]);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void UnmarkBucketForWriting(State state, int versionIndex, int prev) =>
    UnmarkBucketForWriting(ref state.bucketVersions[versionIndex], prev);

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static int MarkBucketForWriting(ref int version)
  {
    if (Atomic.IsWeakMemoryOrdering)
    {
      return Interlocked.Or(ref version, int.MinValue);
    }
    else
    {
      var current = Volatile.Read(ref version);
      Volatile.Write(ref version, current | int.MinValue);
      return current;
    }
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static void UnmarkBucketForWriting(ref int version, int prevVersion)
  {
    Volatile.Write(ref version, (prevVersion + 1) & int.MaxValue); // <-- release: all previous writes are completed before unmarking
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static bool IsMarkedForWriting(ref int bucketVersion, out int version)
  {
    version = Volatile.Read(ref bucketVersion);  // <-- acquire: version check goes before any attempts to read the data structure

    return version < 0;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static bool TryReadEntry(State state, int index, int versionIndex, int versionBefore, out Entry entry)
  {
    entry = state.entries[index];

    if (Atomic.IsWeakMemoryOrdering)
      Interlocked.MemoryBarrier(); // <-- prevent escaping Entry reading after version check

    return Volatile.Read(ref state.bucketVersions[versionIndex]) == versionBefore;
  }

  /// <remarks>
  /// <c>entry.key</c> and <c>entry.value</c> are not loaded when <paramref name="hashCodeEquals"/> is <c>false</c>
  /// </remarks>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private static bool TryReadEntryExpectingHash(State state, int index, ref int version, int versionBefore, out Entry entry,
    uint hash, out bool hashCodeEquals)
  {
    Unsafe.SkipInit(out entry);
    ref var e = ref state.entries[index];
    entry.hash = e.hash;
    entry.next = e.next;
    hashCodeEquals = entry.hash == hash;
    if (hashCodeEquals)
    {
      entry.key = e.key;
      entry.value = e.value;
    }

    if (Atomic.IsWeakMemoryOrdering)
      Interlocked.MemoryBarrier(); // <-- prevent escaping Entry reading after version check

    return Volatile.Read(ref version) == versionBefore;
  }

  #endregion

  [StructLayout(LayoutKind.Auto)]
  private struct Entry
  {
    public uint hash;
    public int next;
    public TValue? value;
    public TKey? key;
  }

  private class State
  {
    public State(int size)
    {
      entries = new Entry[size];
      buckets = new int[size];
      bucketVersions = new int[size / BucketsPerVersion + 1];

      for (int i = 0; i < size; i++)
      {
        buckets[i] = -1;
        entries[i].hash = uint.MaxValue;
      }
    }

    public readonly Entry[] entries;
    public readonly int[] buckets;
    public readonly int[] bucketVersions;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Index(uint value) => value % (uint)buckets.Length;
  }

  public void Clear()
  {
    state = new(HashHelpers.GetPrime(Math.Max(3, Capacity)));
    count = 0;
    freeCount = 0;
    freeList = -1;
  }
}