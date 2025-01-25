// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Collections;

/// <summary>
/// Memory compact thread-safe (multiple producers, multiple consumers) hash map.
/// </summary>
/// <typeparam name="TKey">The type of dictionary key.</typeparam>
/// <typeparam name="TValue">The type of values stored in the dictionary.</typeparam>
/// <typeparam name="TComparer">
/// The <see cref="IEqualityComparer{T}"/> for <typeparamref name="TKey"/> comparison.
/// <para>
/// Passed as <c>struct</c> for performance reasons.<br/>
/// Use <see cref="DefaultComparer{TKey}"/> for default comparer, or <see cref="ComparerWrapper{TKey}"/> for reference type comparers.
/// </para>
/// </typeparam>
/// <remarks>
/// <para>
/// Memory-compact (like non thread-safe <see cref="Dictionary{TKey,TValue}"/>): data is stored in arrays that are only reallocated on resizes.
/// </para>
/// <para>
/// Not lockless for both write and read operations: writes happen under per-segment lock, reads may be retried when a parallel update occurs. It is generally is slower than <see cref="ConcurrentDictionary{TKey,TValue}"/>, especially for reads, but helps to beat GC overhead.
/// </para>
/// <para>
/// Not sealed to allow creation of inheritor without <typeparamref name="TComparer"/> type parameter.
/// </para>
/// </remarks>
public partial class StripedDictionary<TKey, TValue, TComparer> : IDictionary<TKey, TValue>,
  IReadOnlyDictionary<TKey, TValue>
  where TComparer : struct, IEqualityComparer<TKey>
  where TKey : notnull
{
  private WrappedComparer<TKey, TComparer> comparer;

  private const uint HashCodesMask = int.MaxValue;

  private readonly SingleWriterSegment<TKey, TValue, TComparer>[] segments;

  /// <param name="concurrencyLevel">A number of segments and locks. Each segment are protected with a write lock.</param>
  /// <param name="initialSegmentCapacity">An initial capacity of the each segment.</param>
  /// <param name="comparer">
  /// The comparer for <typeparamref name="TKey"/> comparison.
  /// <para>
  /// Passed as <c>struct</c> for performance reasons.<br/>
  /// Use <see cref="DefaultComparer{TKey}"/> for default comparer, or <see cref="ComparerWrapper{TKey}"/> for reference type comparers.
  /// </para>
  /// </param>
  public StripedDictionary(int concurrencyLevel = 31, int initialSegmentCapacity = 16, TComparer comparer = default)
  {
    this.comparer = new(comparer);
    segments = new SingleWriterSegment<TKey, TValue, TComparer>[HashHelpers.GetConcurrencyLevel(concurrencyLevel)];

    for (int i = 0; i < segments.Length; i++)
      segments[i] = new(initialSegmentCapacity, this.comparer);

    Keys = new KeysCollection<TKey, TValue>(this);
    Values = new ValuesCollection<TKey, TValue>(this);
  }

  /// <param name="enumerable">An enumerable of key-value pairs which will be added to the dictionary.</param>
  /// <inheritdoc cref="M:ConcurrencyToolkit.Collections.StripedDictionary`3.#ctor(System.Int32,System.Int32,`2)"/>
  public StripedDictionary(IEnumerable<KeyValuePair<TKey, TValue>> enumerable, int concurrencyLevel = 31,
    int initialSegmentCapacity = 16, TComparer comparer = default)
    : this(concurrencyLevel, initialSegmentCapacity, comparer)
  {
    foreach (var (key, value) in enumerable)
      Add(key, value);
  }

  void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

  public void Clear()
  {
    foreach (ref var segment in segments.AsSpan())
    {
      lock (segment.SyncObject)
        segment.Clear();
    }
  }

  bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
  {
    var hash = ComputeHash(item.Key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)currentSegments.Length];

    return segment.TryGetValue<TKey, DirectEquality>(item.Key, hash, out var value) &&
           EqualityComparer<TValue>.Default.Equals(value, item.Value);
  }

  public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
  {
    foreach (var pair in this)
    {
      array[arrayIndex++] = pair;
    }
  }

  bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
  {
    var hash = ComputeHash(item.Key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)currentSegments.Length];

    lock (segment.SyncObject)
      return segment.Remove<TKey, DirectEquality>(item.Key, item.Value, hash);
  }

  public int Count
  {
    get
    {
      int sum = 0;
      foreach (ref var segment in segments.AsSpan())
        sum += segment.Count;

      return sum;
    }
  }

  public long LongCount
  {
    get
    {
      long sum = 0;
      foreach (ref var segment in segments.AsSpan())
        sum += segment.Count;

      return sum;
    }
  }

  bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

  public long Capacity
  {
    get
    {
      long sum = 0;
      foreach (ref var x in segments.AsSpan())
        sum += x.Capacity;

      return sum;
    }
  }

  public int ConcurrencyLevel => segments.Length;

  #region Add, Set

  public bool TryAdd(TKey key, TValue value) => Insert<RefuseModifyPolicy, TKey, DirectEquality>(key, value);

  public void Add(TKey key, TValue value)
  {
    if (!Insert<RefuseModifyPolicy, TKey, DirectEquality>(key, value))
      ThrowHelper.KeyAlreadyExists(key);
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private bool Insert<TOverridePolicy, TAlternateKey, TEquality>(TAlternateKey key, TValue value)
    where TOverridePolicy : struct, IModifyPolicy
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)currentSegments.Length];

    lock (segment.SyncObject)
      return segment.Insert<TOverridePolicy, TAlternateKey, TEquality>(key, value, hash);
  }

  #endregion

  #region Remove

  public bool Remove(TKey key) => Remove<TKey, DirectEquality>(key);

  private bool Remove<TAlternateKey, TEquality>(TAlternateKey key)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    lock (segment.SyncObject)
      return segment.Remove<TAlternateKey, TEquality>(key, hash, out _);
  }

  #endregion

  #region Search

  public bool TryGetValue(TKey key, out TValue value) => TryGetValue<TKey, DirectEquality>(key, out value);

  private bool TryGetValue<TAlternateKey, TEquality>(TAlternateKey key, out TValue value)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    if (segment.TryGetValue<TAlternateKey, TEquality>(key, hash, out value))
      return true;

    value = default;
    return false;
  }

  public bool ContainsKey(TKey key) => ContainsKey<TKey, DirectEquality>(key);

  private bool ContainsKey<TAlternateKey, TEquality>(TAlternateKey key)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)currentSegments.Length];

    return segment.TryGetValue<TAlternateKey, TEquality>(key, hash, out _);
  }

  #endregion

  #region Indexer

  public TValue this[TKey key]
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => TryGetValue(key, out var value) ? value : ThrowHelper.KeyNotFound<TKey, TValue>(key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    set => Insert<CanModifyPolicy, TKey, DirectEquality>(key, value);
  }

  IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

  IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

  public ICollection<TKey> Keys { get; }
  public ICollection<TValue> Values { get; }
  public bool IsEmpty => Count == 0;

  #endregion

  #region Enumeration

  public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
  {
    var currentSegments = segments;

    for (var i = 0; i < currentSegments.Length; ++i)
    {
      foreach (var pair in currentSegments[i])
        yield return pair;
    }
  }

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  #endregion

  #region Helper methods for segment navigation

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private uint ComputeHash(TKey key)
  {
    ArgumentNullException.ThrowIfNull(key);
    return (uint)comparer.GetHashCode(key) & HashCodesMask;
  }

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  private uint ComputeHash<TAlternateKey, TEquality>(TAlternateKey key)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    ThrowHelper.ThrowIfNull(key);
    return (uint)comparer.GetHashCode<TAlternateKey, TEquality>(key) & HashCodesMask;
  }

  #endregion

  public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue) =>
    TryUpdate<TKey, DirectEquality>(key, newValue, comparisonValue);

  private bool TryUpdate<TAlternateKey, TEquality>(TAlternateKey key, TValue newValue, TValue comparisonValue)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    lock (segment.SyncObject)
    {
      if (segment.TryGetValueUnsafe<TAlternateKey, TEquality>(key, hash, out var value) &&
          EqualityComparer<TValue>.Default.Equals(value, comparisonValue))
      {
        segment.Insert<CanModifyPolicy, TAlternateKey, TEquality>(key, newValue, hash);
        return true;
      }
    }

    return false;
  }

  public bool TryRemove(TKey key, out TValue value) => TryRemove<TKey, DirectEquality>(key, out value);

  private bool TryRemove<TAlternateKey, TEquality>(TAlternateKey key, out TValue value)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    lock (segment.SyncObject)
      return segment.Remove<TAlternateKey, TEquality>(key, hash, out value);
  }

  public bool TryRemove(KeyValuePair<TKey, TValue> pair) => TryRemove<TKey, DirectEquality>(pair.Key, pair.Value);

  private bool TryRemove<TAlternateKey, TEquality>(TAlternateKey key, TValue value)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    lock (segment.SyncObject)
      return segment.Remove<TAlternateKey, TEquality>(key, value, hash);
  }

  public TValue GetOrAdd(TKey key, TValue value) => GetOrAdd<TKey, DirectEquality>(key, value);

  private TValue GetOrAdd<TAlternateKey, TEquality>(TAlternateKey key, TValue value)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    if (segment.TryGetValue<TAlternateKey, TEquality>(key, hash, out var existingValue))
      return existingValue;

    lock (segment.SyncObject)
    {
      if (segment.TryGetValueUnsafe<TAlternateKey, TEquality>(key, hash, out existingValue))
        return existingValue;
      segment.Insert<RefuseModifyPolicy, TAlternateKey, TEquality>(key, value, hash);
      return value;
    }
  }

  public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    => GetOrAdd<TKey, DirectEquality>(key, valueFactory);

  private TValue GetOrAdd<TAlternateKey, TEquality>(TAlternateKey key, Func<TAlternateKey, TValue> valueFactory)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    ArgumentNullException.ThrowIfNull(valueFactory);
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    if (segment.TryGetValue<TAlternateKey, TEquality>(key, hash, out var existingValue))
      return existingValue;

    var value = valueFactory(key);

    lock (segment.SyncObject)
    {
      if (segment.TryGetValueUnsafe<TAlternateKey, TEquality>(key, hash, out existingValue))
        return existingValue;
      segment.Insert<RefuseModifyPolicy, TAlternateKey, TEquality>(key, value, hash);
      return value;
    }
  }

  public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TArg factoryArgument)
    => GetOrAdd<TKey, DirectEquality, TArg>(key, valueFactory, factoryArgument);

  private TValue GetOrAdd<TAlternateKey, TEquality, TArg>(TAlternateKey key,
    Func<TAlternateKey, TArg, TValue> valueFactory, TArg factoryArgument)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    ArgumentNullException.ThrowIfNull(valueFactory);
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    if (segment.TryGetValue<TAlternateKey, TEquality>(key, hash, out var existingValue))
      return existingValue;

    var value = valueFactory(key, factoryArgument);

    lock (segment.SyncObject)
    {
      if (segment.TryGetValueUnsafe<TAlternateKey, TEquality>(key, hash, out existingValue))
        return existingValue;
      segment.Insert<RefuseModifyPolicy, TAlternateKey, TEquality>(key, value, hash);
      return value;
    }
  }

  public TValue AddOrUpdate<TArg>(
    TKey key, Func<TKey, TArg, TValue> addValueFactory, Func<TKey, TValue, TArg, TValue> updateValueFactory,
    TArg factoryArgument) =>
    AddOrUpdate<TKey, DirectEquality, TArg>(key, addValueFactory, updateValueFactory, factoryArgument);

  private TValue AddOrUpdate<TAlternateKey, TEquality, TArg>(
    TAlternateKey key, Func<TAlternateKey, TArg, TValue> addValueFactory,
    Func<TAlternateKey, TValue, TArg, TValue> updateValueFactory,
    TArg factoryArgument)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    ArgumentNullException.ThrowIfNull(addValueFactory);
    ArgumentNullException.ThrowIfNull(updateValueFactory);
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    while (true)
    {
      if (!segment.TryGetValue<TAlternateKey, TEquality>(key, hash, out var existingValue))
      {
        var addValue = addValueFactory(key, factoryArgument);
        lock (segment.SyncObject)
        {
          if (!segment.TryGetValueUnsafe<TAlternateKey, TEquality>(key, hash, out existingValue))
          {
            segment.Insert<RefuseModifyPolicy, TAlternateKey, TEquality>(key, addValue, hash);
            return addValue;
          }
        }
      }
      else
      {
        var newValue = updateValueFactory(key, existingValue, factoryArgument);
        lock (segment.SyncObject)
        {
          if (segment.Update<TAlternateKey, TEquality>(key, newValue, existingValue, hash))
            return newValue;
        }
      }
    }
  }

  public TValue AddOrUpdate(TKey key, Func<TKey, TValue> addValueFactory, Func<TKey, TValue, TValue> updateValueFactory)
    => AddOrUpdate(key, addValueFactory, updateValueFactory);

  private TValue AddOrUpdate<TAlternateKey, TEquality>(TAlternateKey key, Func<TAlternateKey, TValue> addValueFactory,
    Func<TAlternateKey, TValue, TValue> updateValueFactory)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    ArgumentNullException.ThrowIfNull(addValueFactory);
    ArgumentNullException.ThrowIfNull(updateValueFactory);
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    while (true)
    {
      if (!segment.TryGetValue<TAlternateKey, TEquality>(key, hash, out var existingValue))
      {
        var addValue = addValueFactory(key);
        lock (segment.SyncObject)
        {
          if (!segment.TryGetValueUnsafe<TAlternateKey, TEquality>(key, hash, out existingValue))
          {
            segment.Insert<RefuseModifyPolicy, TAlternateKey, TEquality>(key, addValue, hash);
            return addValue;
          }
        }
      }
      else
      {
        var newValue = updateValueFactory(key, existingValue);
        lock (segment.SyncObject)
        {
          if (segment.Update<TAlternateKey, TEquality>(key, newValue, existingValue, hash))
            return newValue;
        }
      }
    }
  }

  public TValue AddOrUpdate(TKey key, TValue addValue, Func<TKey, TValue, TValue> updateValueFactory)
    => AddOrUpdate<TKey, DirectEquality>(key, addValue, updateValueFactory);

  private TValue AddOrUpdate<TAlternateKey, TEquality>(TAlternateKey key, TValue addValue,
    Func<TAlternateKey, TValue, TValue> updateValueFactory)
    where TEquality : struct, IEquality
#if NET9_0_OR_GREATER
    where TAlternateKey : allows ref struct
#endif
  {
    ArgumentNullException.ThrowIfNull(updateValueFactory);
    var hash = ComputeHash<TAlternateKey, TEquality>(key);

    var currentSegments = segments;

    ref var segment = ref currentSegments[hash % (uint)segments.Length];

    while (true)
    {
      if (!segment.TryGetValue<TAlternateKey, TEquality>(key, hash, out var existingValue))
      {
        lock (segment.SyncObject)
        {
          if (!segment.TryGetValueUnsafe<TAlternateKey, TEquality>(key, hash, out existingValue))
          {
            segment.Insert<RefuseModifyPolicy, TAlternateKey, TEquality>(key, addValue, hash);
            return addValue;
          }
        }
      }
      else
      {
        var newValue = updateValueFactory(key, existingValue);
        lock (segment.SyncObject)
        {
          if (segment.Update<TAlternateKey, TEquality>(key, newValue, existingValue, hash))
            return newValue;
        }
      }
    }
  }

#if NET9_0_OR_GREATER
  public bool TryGetAlternateLookup<TAlternateKey>(out AlternateLookup<TAlternateKey> lookup)
    where TAlternateKey : allows ref struct
  {
    if (!comparer.IsCompatibleKey<TAlternateKey>())
    {
      lookup = default;
      return false;
    }

    lookup = new AlternateLookup<TAlternateKey>(this);
    return true;
  }
#endif
}