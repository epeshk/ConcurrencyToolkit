// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections;
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Collections;

public partial class SingleWriterDictionary<TKey, TValue, TComparer>
{
  /// <inheritdoc cref="SingleWriterDictionary{TKey,TValue,TComparer}.Reader"/>
  public sealed class DictionaryReader : IReadOnlyDictionary<TKey, TValue>
  {
    private readonly SingleWriterDictionary<TKey, TValue, TComparer> that;
    private TComparer comparer;

    internal DictionaryReader(SingleWriterDictionary<TKey, TValue, TComparer> that, TComparer comparer)
    {
      this.that = that;
      this.comparer = comparer;
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => that.segment.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public int Count => that.segment.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TKey key, out TValue value) =>
      that.segment.TryGetValue(key, ComputeHash(key), out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ComputeHash(TKey key)
    {
      ArgumentNullException.ThrowIfNull(key);
      return (uint)comparer.GetHashCode(key) & HashCodesMask;
    }

    public TValue this[TKey key]
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => TryGetValue(key, out var value) ? value : ThrowHelper.KeyNotFound<TKey, TValue>(key);
    }

    public IEnumerable<TKey> Keys => this.Select(x => x.Key);
    public IEnumerable<TValue> Values => this.Select(x => x.Value);
    public int Capacity => that.segment.Capacity;
  }
}