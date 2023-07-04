// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

namespace ConcurrencyToolkit.Collections;

internal sealed class KeysCollection<TKey, TValue> : DictionaryCollection<TKey, TValue, TKey>
{
  public KeysCollection(IReadOnlyDictionary<TKey, TValue> dict) : base(dict, static x => x.Key)
  {
  }
}