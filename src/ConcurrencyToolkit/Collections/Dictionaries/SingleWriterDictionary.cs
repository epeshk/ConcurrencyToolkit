// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

namespace ConcurrencyToolkit.Collections;

/// <summary>
/// Single producer multiple consumers hash map.
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
/// Writes are lock-free (except for resizes, which may trigger GC), but reads may be retried when a parallel update occurs.
/// </para>
/// </remarks>
public partial class SingleWriterDictionary<TKey, TValue, TComparer>
  where TComparer : struct, IEqualityComparer<TKey>
  where TKey : notnull
{
  private SingleWriterSegment<TKey, TValue, TComparer> segment;

  /// <summary>
  /// A reader facade for <see cref="SingleWriterDictionary{TKey,TValue,TComparer}"/>. May be used in parallel.
  /// </summary>
  public DictionaryReader Reader { get; }

  /// <summary>
  /// <para>
  /// A writer facade for <see cref="SingleWriterDictionary{TKey,TValue,TComparer}"/>. MUST NOT be used in parallel.
  /// </para>
  /// <para>
  /// Writer also has read access to the dictionary. Read operations performed by Writer operate under the write exclusivity assumption: no parallel modifications are allowed.
  /// <br/>
  /// Thus, the writer thread may read the dictionary without synchronization overhead.
  /// </para>
  /// </summary>
  public DictionaryWriter Writer { get; }

  private const uint HashCodesMask = int.MaxValue;

  public SingleWriterDictionary(int capacity = 128, TComparer comparer = default)
  {
    segment = new(capacity, comparer);

    Reader = new(this, comparer);
    Writer = new(this, comparer);
  }
}
