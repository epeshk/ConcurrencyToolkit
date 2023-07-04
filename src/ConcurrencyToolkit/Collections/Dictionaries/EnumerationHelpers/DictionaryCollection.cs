// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Collections;

namespace ConcurrencyToolkit.Collections;

internal class DictionaryCollection<TKey, TValue, TElement> : ICollection<TElement>
{
  private readonly IReadOnlyDictionary<TKey, TValue> dict;
  private readonly Func<KeyValuePair<TKey, TValue>, TElement> elementSelector;

  protected DictionaryCollection(IReadOnlyDictionary<TKey, TValue> dict, Func<KeyValuePair<TKey, TValue>, TElement> elementSelector)
  {
    this.dict = dict;
    this.elementSelector = elementSelector;
  }

  public IEnumerator<TElement> GetEnumerator() => dict.Select(elementSelector).GetEnumerator();

  IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

  public void Add(TElement item) => throw new NotSupportedException();

  public void Clear() => throw new NotSupportedException();

  public bool Contains(TElement item) => throw new NotSupportedException();

  public void CopyTo(TElement[] array, int arrayIndex)
  {
    var index = arrayIndex;
    if (arrayIndex + Count > array.Length) throw new ArgumentException("Provided array is too small to fit the collection.");
    foreach (var key in this)
    {
      if (index >= array.Length) throw new ArgumentException("Provided array is too small to fit the collection. New elements was added to the collection concurrently.");
      array[index++] = key;
    }
  }

  public bool Remove(TElement item) => throw new NotSupportedException();

  public int Count => dict.Count;
  public bool IsReadOnly => true;
}