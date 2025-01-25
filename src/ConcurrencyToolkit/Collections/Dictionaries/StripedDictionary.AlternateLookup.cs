// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

#if NET9_0_OR_GREATER
using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Internal;

namespace ConcurrencyToolkit.Collections;

public partial class StripedDictionary<TKey, TValue, TComparer>
{
  public readonly struct AlternateLookup<TAlternateKey> where TAlternateKey : allows ref struct
  {
    private readonly StripedDictionary<TKey, TValue, TComparer> that;

    public AlternateLookup(StripedDictionary<TKey, TValue, TComparer> that) => this.that = that;

    public bool TryAdd(TAlternateKey key, TValue value) => that.Insert<RefuseModifyPolicy, TAlternateKey, AlternateEquality>(key, value);

    public void Add(TAlternateKey key, TValue value)
    {
      if (!that.Insert<RefuseModifyPolicy, TAlternateKey, AlternateEquality>(key, value))
        ThrowHelper.KeyAlreadyExists(that.comparer.CreateKey<TAlternateKey, AlternateEquality>(key));
    }

    public bool Remove(TAlternateKey key)
      => that.Remove<TAlternateKey, AlternateEquality>(key);
    public bool TryGetValue(TAlternateKey key, out TValue value)
      => that.TryGetValue<TAlternateKey, AlternateEquality>(key, out value);
    public bool ContainsKey(TAlternateKey key)
      => that.ContainsKey<TAlternateKey, AlternateEquality>(key);
    public TValue this[TAlternateKey key]
    {
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      get => that.TryGetValue<TAlternateKey, AlternateEquality>(key, out var value) ? value : ThrowHelper.KeyNotFound<TKey, TValue>(that.comparer.CreateKey<TAlternateKey, AlternateEquality>(key));

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      set => that.Insert<CanModifyPolicy, TAlternateKey, AlternateEquality>(key, value);
    }

    public bool TryUpdate(TAlternateKey key, TValue newValue, TValue comparisonValue)
      => that.TryUpdate<TAlternateKey, AlternateEquality>(key, newValue, comparisonValue);

    public bool TryRemove(TAlternateKey key, out TValue value)
      => that.TryRemove<TAlternateKey, AlternateEquality>(key, out value);

    public bool TryRemove(TAlternateKey key, TValue value)
      => that.TryRemove<TAlternateKey, AlternateEquality>(key, value);

    public TValue GetOrAdd(TAlternateKey key, TValue value)
      => that.GetOrAdd<TAlternateKey, AlternateEquality>(key, value);

    public TValue GetOrAdd(TAlternateKey key, Func<TAlternateKey, TValue> valueFactory)
      => that.GetOrAdd<TAlternateKey, AlternateEquality>(key, valueFactory);

    public TValue GetOrAdd<TArg>(TAlternateKey key, Func<TAlternateKey, TArg, TValue> valueFactory, TArg factoryArgument)
      => that.GetOrAdd<TAlternateKey, AlternateEquality, TArg>(key, valueFactory, factoryArgument);

    public TValue AddOrUpdate<TArg>(
      TAlternateKey key, Func<TAlternateKey, TArg, TValue> addValueFactory, Func<TAlternateKey, TValue, TArg, TValue> updateValueFactory,
      TArg factoryArgument)
      => that.AddOrUpdate<TAlternateKey, AlternateEquality, TArg>(key, addValueFactory, updateValueFactory, factoryArgument);

    public TValue AddOrUpdate(TAlternateKey key, Func<TAlternateKey, TValue> addValueFactory,
      Func<TAlternateKey, TValue, TValue> updateValueFactory)
      => that.AddOrUpdate<TAlternateKey, AlternateEquality>(key, addValueFactory, updateValueFactory);

    public TValue AddOrUpdate(TAlternateKey key, TValue addValue, Func<TAlternateKey, TValue, TValue> updateValueFactory)
      => that.AddOrUpdate<TAlternateKey, AlternateEquality>(key, addValue, updateValueFactory);
  }
}

#endif