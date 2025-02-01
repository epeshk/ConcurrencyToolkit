using ConcurrencyToolkit.Collections;
using FluentAssertions;

namespace ConcurrencyToolkit.Tests.Collections;

public class SingleWriterDictionaryTests
{
  [TestFixture]
  public class WriterTests
  {
    private SingleWriterDictionary<int, string, DefaultComparer<int>> dictionary;

    [SetUp]
    public void Setup() => dictionary = new(128);

    [Test]
    public void Add_ShouldInsertKeyValue()
    {
      dictionary.Writer.Add(1, "one");
      dictionary.Writer.TryGetValue(1, out var value).Should().BeTrue();
      value.Should().Be("one");
    }

    [Test]
    public void TryAdd_ExistingKey_ReturnsFalse()
    {
      dictionary.Writer.Add(1, "one");
      dictionary.Writer.TryAdd(1, "two").Should().BeFalse();
    }

    [Test]
    public void Indexer_Set_UpdatesValue()
    {
      dictionary.Writer.Add(1, "one");
      dictionary.Writer[1] = "updated";
      dictionary.Writer[1].Should().Be("updated");
    }

    [Test]
    public void Remove_ExistingKey_ReturnsTrue()
    {
      dictionary.Writer.Add(1, "one");
      dictionary.Writer.Remove(1).Should().BeTrue();
      dictionary.Writer.ContainsKey(1).Should().BeFalse();
    }

    [Test]
    public void Clear_RemovesAllItems()
    {
      dictionary.Writer.Add(1, "one");
      dictionary.Writer.Add(2, "two");
      dictionary.Writer.Clear();
      dictionary.Writer.Count.Should().Be(0);
    }

    [Test]
    public void Count_ReflectsNumberOfItems()
    {
      dictionary.Writer.Add(1, "one");
      dictionary.Writer.Add(2, "two");
      dictionary.Writer.Count.Should().Be(2);
    }

    [Test]
    public void Capacity_IncreasesWhenNeeded()
    {
      int initialCapacity = dictionary.Writer.Capacity;
      for (int i = 0; i < initialCapacity + 1; i++)
        dictionary.Writer.Add(i, $"value{i}");

      dictionary.Writer.Capacity.Should().BeGreaterThan(initialCapacity);
    }


    [Test]
    public void CopyTo_CopiesAllElements()
    {
      dictionary.Writer.Add(1, "one");
      dictionary.Writer.Add(2, "two");
      var array = new KeyValuePair<int, string>[2];

      ((ICollection<KeyValuePair<int, string>>)dictionary.Writer).CopyTo(array, 0);

      array.Should().BeEquivalentTo(new[]
      {
        new KeyValuePair<int, string>(1, "one"),
        new KeyValuePair<int, string>(2, "two")
      });
    }

    [Test]
    public void Remove_KeyValuePair_ValueMismatch_ReturnsFalse()
    {
      dictionary.Writer.Add(1, "one");
      var pair = new KeyValuePair<int, string>(1, "wrong");
      dictionary.Writer.Remove(pair).Should().BeFalse();
    }

    [Test]
    public void Remove_KeyValuePair_ValueMatch_ReturnsTrue()
    {
      dictionary.Writer.Add(1, "one");
      var pair = new KeyValuePair<int, string>(1, "one");
      dictionary.Writer.Remove(pair).Should().BeTrue();
    }

    [Test]
    public void Indexer_GetNonExistentKey_Throws()
    {
      dictionary.Writer.Invoking(w => _ = w[99])
        .Should().Throw<KeyNotFoundException>();
    }
  }

  [TestFixture]
  public class ReaderTests
  {
    private SingleWriterDictionary<int, string, DefaultComparer<int>> dictionary;

    [SetUp]
    public void Setup()
    {
      dictionary = new(128);
      dictionary.Writer.Add(1, "one");
      dictionary.Writer.Add(2, "two");
    }

    [Test]
    public void TryGetValue_ExistingKey_ReturnsTrue()
    {
      dictionary.Reader.TryGetValue(1, out var value).Should().BeTrue();
      value.Should().Be("one");
    }

    [Test]
    public void ContainsKey_NonExistent_ReturnsFalse()
    {
      dictionary.Reader.ContainsKey(99).Should().BeFalse();
    }

    [Test]
    public void Enumeration_ReturnsAllItems()
    {
      var items = new List<KeyValuePair<int, string>>();
      foreach (var kvp in dictionary.Reader)
        items.Add(kvp);

      items.Should().BeEquivalentTo(new[]
      {
        new KeyValuePair<int, string>(1, "one"),
        new KeyValuePair<int, string>(2, "two")
      });
    }

    [Test]
    public void Indexer_GetNonExistentKey_Throws()
    {
      dictionary.Reader.Invoking(w => _ = w[99])
        .Should().Throw<KeyNotFoundException>();
    }

  }

#if NET9_0_OR_GREATER
  [TestFixture]
  public class AlternateKeyTests
  {
    private readonly record struct IntWrapper(int value) : IEqualityComparer<int>, IAlternateEqualityComparer<IntWrapper, int>
    {
      public bool Equals(IntWrapper x, IntWrapper y) => x.value == y.value;
      public bool Equals(IntWrapper alternate, int other) => alternate.value == other;

      public int GetHashCode(IntWrapper obj) => obj.value.GetHashCode();
      public int Create(IntWrapper alternate) => alternate.value;

      public bool Equals(int x, int y) => x == y;

      public int GetHashCode(int obj) => obj.GetHashCode();
    }

    private SingleWriterDictionary<int, string, ComparerWrapper<int>> dictionary;

    [SetUp]
    public void Setup() => dictionary = new(128, new(new IntWrapper()));

    [Test]
    public void TryGetAlternateLookup_WithCompatibleKey_ReturnsTrue()
    {
      dictionary.Writer.TryGetAlternateWriter<IntWrapper>(out var lookup).Should().BeTrue();
      dictionary.Reader.Should().NotBeNull();
    }

    [Test]
    public void AlternateReader_GetValue_Works()
    {
      dictionary.Writer.Add(42, "answer");
      dictionary.TryGetAlternateLookup<IntWrapper>(out var lookup);

      lookup.Reader.TryGetValue(new IntWrapper(42), out var value).Should().BeTrue();
      value.Should().Be("answer");
    }

    [Test]
    public void AlternateWriter_Add_Works()
    {
      dictionary.Writer.TryGetAlternateWriter<IntWrapper>(out var lookup);
      lookup.Add(new IntWrapper(42), "answer");

      dictionary.Reader.TryGetValue(42, out var value).Should().BeTrue();
      value.Should().Be("answer");
    }

    [Test]
    public void AlternateLookup()
    {
      var dict = new SingleWriterDictionary<string, string, ComparerWrapper<string>>(comparer: new(StringComparer.Ordinal));
      dict.Writer["abc"] = "xyz";

      dict.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup).Should().BeTrue();
      Span<char> key = stackalloc char[3];
      key[0] = 'a';
      key[1] = 'b';
      key[2] = 'c';
      lookup.Reader[key].Should().Be("xyz");
      lookup.Writer[key] = "123";
      lookup.Reader[key].Should().Be("123");
    }
  }
#endif
}