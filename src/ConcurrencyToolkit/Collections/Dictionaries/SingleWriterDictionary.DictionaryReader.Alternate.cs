// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

#if NET9_0_OR_GREATER
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Collections;

public partial class SingleWriterDictionary<TKey, TValue, TComparer>
{
  /// <inheritdoc cref="SingleWriterDictionary{TKey,TValue,TComparer}.Reader"/>
  public readonly struct AlternateReader<TAlternateKey>
    where TAlternateKey : allows ref struct
  {
    private readonly SingleWriterDictionary<TKey, TValue, TComparer> that;

    internal AlternateReader(SingleWriterDictionary<TKey, TValue, TComparer> that) => this.that = that;

    public int Count => that.segment.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(TAlternateKey key) => TryGetValue(key, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TAlternateKey key, out TValue value) =>
      that.segment.TryGetValue<TAlternateKey, AlternateEquality>(key, ComputeHash(key), out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ComputeHash(TAlternateKey key)
    {
      ThrowHelper.ThrowIfNull(key);
      return (uint)that.segment.comparer.GetHashCode<TAlternateKey, AlternateEquality>(key) & HashCodesMask;
    }

    public TValue this[TAlternateKey key]
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => TryGetValue(key, out var value)
        ? value
        : ThrowHelper.KeyNotFound<TKey, TValue>(that.segment.comparer.CreateKey<TAlternateKey, AlternateEquality>(key));
    }
  }
}

#endif