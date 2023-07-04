// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections;
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Collections;

public partial class SingleWriterDictionary<TKey, TValue, TComparer>
{
  /// <inheritdoc cref="SingleWriterDictionary{TKey,TValue,TComparer}.Writer"/>
  public sealed class DictionaryWriter : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
  {
    private readonly SingleWriterDictionary<TKey, TValue, TComparer> that;
    private TComparer comparer;

    internal DictionaryWriter(SingleWriterDictionary<TKey, TValue, TComparer> that, TComparer comparer)
    {
      this.that = that;
      this.comparer = comparer;

      Keys = new KeysCollection<TKey, TValue>(this);
      Values = new ValuesCollection<TKey, TValue>(this);
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() =>
      that.segment.EnumerateUnsafe().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);

    public void Clear() => that.segment.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) =>
      TryGetValue(item.Key, out var value) &&
      EqualityComparer<TValue>.Default.Equals(item.Value, value);

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
      var index = arrayIndex;
      if (arrayIndex + Count > array.Length) throw new ArgumentException("Provided array is too small to fit the collection.");
      foreach (var key in this) array[index++] = key;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(KeyValuePair<TKey, TValue> item) =>
      that.segment.Remove(item, ComputeHash(item.Key));

    public int Count => that.segment.Count;
    public bool IsReadOnly => false;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(TKey key, TValue value)
    {
      if (!TryAdd(key, value))
        ThrowHelper.KeyAlreadyExists(key);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(TKey key, TValue value) => that.segment.Insert<RefuseModifyPolicy>(key, value, ComputeHash(key));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(TKey key) => that.segment.Remove(key, ComputeHash(key), out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TKey key, out TValue value) =>
      that.segment.TryGetValueUnsafe(key, ComputeHash(key), out value);

    public TValue this[TKey key]
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => TryGetValue(key, out var value) ? value : ThrowHelper.KeyNotFound<TKey, TValue>(key);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      set => that.segment.Insert<CanModifyPolicy>(key, value, (uint)comparer.GetHashCode(key) & HashCodesMask);
    }

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    public ICollection<TKey> Keys { get; }
    public ICollection<TValue> Values { get; }
    public int Capacity => that.segment.Capacity;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ComputeHash(TKey key)
    {
      ArgumentNullException.ThrowIfNull(key);
      return (uint)comparer.GetHashCode(key) & HashCodesMask;
    }
  }
}