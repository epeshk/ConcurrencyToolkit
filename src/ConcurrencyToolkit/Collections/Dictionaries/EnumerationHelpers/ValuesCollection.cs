// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

namespace ConcurrencyToolkit.Collections;

internal sealed class ValuesCollection<TKey, TValue> : DictionaryCollection<TKey, TValue, TValue>
{
  public ValuesCollection(IReadOnlyDictionary<TKey, TValue> dict) : base(dict, static x => x.Value)
  {
  }
}