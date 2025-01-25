// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

#if NET9_0_OR_GREATER
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Collections;

public partial class SingleWriterDictionary<TKey, TValue, TComparer>
{
  /// <inheritdoc cref="SingleWriterDictionary{TKey,TValue,TComparer}.Writer"/>
  public readonly struct AlternateWriter<TAlternateKey>
    where TAlternateKey : allows ref struct
  {
    private readonly SingleWriterDictionary<TKey, TValue, TComparer> that;

    internal AlternateWriter(SingleWriterDictionary<TKey, TValue, TComparer> that)
    {
      this.that = that;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(TAlternateKey key, TValue value) =>
      that.segment.Remove<TAlternateKey, AlternateEquality>(key, value, ComputeHash(key));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(TAlternateKey key, TValue value)
    {
      if (!TryAdd(key, value))
        ThrowHelper.KeyAlreadyExists(that.segment.comparer.CreateKey<TAlternateKey, AlternateEquality>(key));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAdd(TAlternateKey key, TValue value) =>
      that.segment.Insert<RefuseModifyPolicy, TAlternateKey, AlternateEquality>(key, value, ComputeHash(key));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(TAlternateKey key) => TryGetValue(key, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(TAlternateKey key) =>
      that.segment.Remove<TAlternateKey, AlternateEquality>(key, ComputeHash(key), out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TAlternateKey key, out TValue value) =>
      that.segment.TryGetValueUnsafe<TAlternateKey, AlternateEquality>(key, ComputeHash(key), out value);

    public TValue this[TAlternateKey key]
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => TryGetValue(key, out var value)
        ? value
        : ThrowHelper.KeyNotFound<TKey, TValue>(that.segment.comparer.CreateKey<TAlternateKey, AlternateEquality>(key));

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      set => that.segment.Insert<CanModifyPolicy, TAlternateKey, AlternateEquality>(key, value, ComputeHash(key));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private uint ComputeHash(TAlternateKey key)
    {
      ThrowHelper.ThrowIfNull(key);
      return (uint)that.segment.comparer.GetHashCode<TAlternateKey, AlternateEquality>(key) & HashCodesMask;
    }
  }
}
#endif