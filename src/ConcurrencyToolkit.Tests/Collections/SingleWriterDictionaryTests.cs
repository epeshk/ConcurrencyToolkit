using ConcurrencyToolkit.Collections;
using FluentAssertions;

namespace ConcurrencyToolkit.Tests.Collections;

public class SingleWriterDictionaryTests
{
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