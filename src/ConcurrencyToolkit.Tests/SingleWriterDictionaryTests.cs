using ConcurrencyToolkit.Collections;
using FluentAssertions;

namespace ConcurrencyToolkit.Tests;

public class SingleWriterDictionaryTests
{
  [Test]
  public void TwoReaders()
  {
    const int Rounds = 100;

    var cts = new CancellationTokenSource();
    var token = cts.Token;

    var dict = new SingleWriterDictionary<Guid, Guid, DefaultComparer<Guid>>();

    var permanentKeys = Enumerable.Range(0, 1000).Select(x => Guid.NewGuid()).ToArray();
    var permanentValue = Guid.NewGuid();

    var keys = Enumerable.Range(0, 100000).Select(x => Guid.NewGuid()).ToArray();
    var readOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();
    var writeOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();
    var deleteOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();

    foreach (var key in permanentKeys)
    {
      dict.Writer[key] = permanentValue;
    }

    var values = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

    var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
    {
      while (!token.IsCancellationRequested)
      {
        foreach (var key in readOrder)
        {
          var get = dict.Reader.TryGetValue(key, out var val);
          if (get && !values.Contains(val)) throw new Exception($"Unexpected value: {val}. Allowed values: {string.Join(", ", values)}");
        }

        foreach (var permanentKey in permanentKeys)
        {
          if (!dict.Reader.TryGetValue(permanentKey, out var v))
            throw new Exception($"Not found permanent value by permanentKey: {permanentKey}");
          if (v != permanentValue)
            throw new Exception("Wrong permanent value by permanentKey: {permanentKey}");
        }
      }
    })).ToArray();

    for (int i = 0; i < Rounds; i++)
    {
      foreach (var key in writeOrder)
      {
        dict.Writer[key] = values[Random.Shared.Next(0, values.Length)];
      }
      foreach (var key in deleteOrder)
      {
        dict.Writer[key] = values[Random.Shared.Next(0, values.Length)];
      }
    }

    cts.Cancel();

    Task.WaitAll(readers);
  }

  [Test]
  public void TwoReaders_Int_DefaultComparer() => TwoReaders_Int(new DefaultComparer<int>());

  [Test]
  public void TwoReaders_Int_ComparerWrapper() => TwoReaders_Int(new ComparerWrapper<int>(EqualityComparer<int>.Default));

  [Test]
  public void TwoReaders_Int_ComparerWrapper_NonDefault() =>
    TwoReaders_Int(new ComparerWrapper<int>(EqualityComparer<int>.Create((x, y) => x % 1000 == y % 1000, x => x % 1000)));

  public void TwoReaders_Int<TComparer>(TComparer comparer) where TComparer : struct, IEqualityComparer<int>
  {
    const int Rounds = 100;

    var cts = new CancellationTokenSource();
    var token = cts.Token;

    var dict = new SingleWriterDictionary<int, int, TComparer>(comparer: comparer);

    var permanentKeys = Enumerable.Range(0, 1000).Select(x => Random.Shared.Next()).ToArray();
    var permanentValue = Random.Shared.Next();

    var keys = Enumerable.Range(0, 100000).Select(x => Random.Shared.Next()).Where(x => !permanentKeys.Contains(x, comparer)).ToArray();
    var readOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();
    var writeOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();
    var deleteOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();

    foreach (var key in permanentKeys)
    {
      dict.Writer[key] = permanentValue;
    }

    var values = new[] { Random.Shared.Next(), Random.Shared.Next(), Random.Shared.Next() };

    var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
    {
      while (!token.IsCancellationRequested)
      {
        foreach (var key in readOrder)
        {
          var get = dict.Reader.TryGetValue(key, out var val);
          if (get && !values.Contains(val)) throw new Exception($"Unexpected value: {val}. Allowed values: {string.Join(", ", values)}");
        }

        foreach (var permanentKey in permanentKeys)
        {
          if (!dict.Reader.TryGetValue(permanentKey, out var v))
            throw new Exception($"Not found permanent value by permanentKey: {permanentKey}");
          if (v != permanentValue)
            throw new Exception("Wrong permanent value by permanentKey: {permanentKey}");
        }
      }
    })).ToArray();

    for (int i = 0; i < Rounds; i++)
    {
      foreach (var key in writeOrder)
      {
        dict.Writer[key] = values[Random.Shared.Next(0, values.Length)];
      }
      foreach (var key in deleteOrder)
      {
        dict.Writer[key] = values[Random.Shared.Next(0, values.Length)];
      }
    }

    cts.Cancel();

    Task.WaitAll(readers);
  }

  [Test]
  public void TwoReaders_Object()
  {
    const int Rounds = 100;

    var cts = new CancellationTokenSource();
    var token = cts.Token;

    var dict = new SingleWriterDictionary<object, object, DefaultComparer<object>>();

    var permanentKeys = Enumerable.Range(0, 1000).Select(x => new object()).ToArray();
    var permanentValue = new object();

    var keys = Enumerable.Range(0, 100000).Select(x => Random.Shared.Next()).ToArray();
    var readOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();
    var writeOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();
    var deleteOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();

    foreach (var key in permanentKeys)
    {
      dict.Writer[key] = permanentValue;
    }

    var values = new[] { new object(), new(), new() };

    var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
    {
      while (!token.IsCancellationRequested)
      {
        foreach (var key in readOrder)
        {
          var get = dict.Reader.TryGetValue(key, out var val);
          if (get && !values.Contains(val)) throw new Exception($"Unexpected value: {val}. Allowed values: {string.Join(", ", values)}");
        }

        foreach (var permanentKey in permanentKeys)
        {
          if (!dict.Reader.TryGetValue(permanentKey, out var v))
            throw new Exception($"Not found permanent value by permanentKey: {permanentKey}");
          if (v != permanentValue)
            throw new Exception("Wrong permanent value by permanentKey: {permanentKey}");
        }
      }
    })).ToArray();

    for (int i = 0; i < Rounds; i++)
    {
      foreach (var key in writeOrder)
      {
        dict.Writer[key] = values[Random.Shared.Next(0, values.Length)];
      }
      foreach (var key in deleteOrder)
      {
        dict.Writer[key] = values[Random.Shared.Next(0, values.Length)];
      }
    }

    cts.Cancel();

    Task.WaitAll(readers);
  }

  [Test]
  public void ReadingOfContendedKey()
  {
    const int Rounds = 10_000_000;

    var cts = new CancellationTokenSource();
    var token = cts.Token;

    var dict = new SingleWriterDictionary<Guid, Guid, DefaultComparer<Guid>>();

    var keys = Enumerable.Range(0, 100000).Select(x => Guid.NewGuid()).ToArray();
    var contendedKey = keys[keys.Length / 2];
    var values = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
    foreach (var key in keys)
    {
      dict.Writer[key] = values[0];
    }

    var reader = Task.Run(() =>
    {
      int success = 0;
      while (!token.IsCancellationRequested)
      {
        if (!dict.Reader.TryGetValue(contendedKey, out var val))
          throw new Exception("Not found");
        if (!values.Contains(val))
          throw new Exception($"Unexpected value: {val}. Allowed values: {string.Join(", ", values)}");
        success++;
      }

      return success;
    });

    for (int i = 0; i < Rounds; i++)
    {
      dict.Writer[contendedKey] = values[Random.Shared.Next(0, values.Length)];
    }

    cts.Cancel();

    Console.WriteLine(reader.GetAwaiter().GetResult());
  }

  [Test]
  public void Enumeration()
  {
    const int Rounds = 100;

    var cts = new CancellationTokenSource();
    var token = cts.Token;

    var dict = new SingleWriterDictionary<Guid, Guid, DefaultComparer<Guid>>();

    var permanentKeys = Enumerable.Range(0, 1000).Select(x => Guid.NewGuid()).ToArray();
    var permanentValue = Guid.NewGuid();

    var keys = Enumerable.Range(0, 100000).Select(x => Guid.NewGuid()).ToArray();
    var writeOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();
    var deleteOrder = keys.OrderBy(x => Random.Shared.Next()).ToArray();

    foreach (var key in permanentKeys)
    {
      dict.Writer[key] = permanentValue;
    }

    var values = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

    var readers = Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
    {
      while (!token.IsCancellationRequested)
      {
        var pk = permanentKeys.ToHashSet();
        var c = 0;
        foreach (var (k, v) in dict.Reader)
        {
          if (pk.Contains(k))
          {
            if (v != permanentValue)
              throw new("Wrong permanent kvp");
          }
          else if (!values.Contains(v))
            throw new($"Unexpected value: {v}. Allowed values: {string.Join(", ", values)}");

          c++;
        }

        Console.WriteLine($"Enumerated {c} elements");
      }
    })).ToArray();

    var pk_set = permanentKeys.ToHashSet();

    for (int i = 0; i < Rounds; i++)
    {
      foreach (var key in writeOrder)
      {
        dict.Writer[key] = values[Random.Shared.Next(0, values.Length)];
      }
      foreach (var key in deleteOrder)
      {
        dict.Writer.Remove(key);
      }

      var count = 0;
      foreach (var (k, v) in dict.Writer)
      {
        count++;
        if (!pk_set.Contains(k))
          throw new("Wrong enumeration through writer. Stale value");
        if (v != permanentValue)
          throw new("Wrong enumeration through writer. Wrong value");
      }

      count.Should().Be(pk_set.Count);
      dict.Writer.Count.Should().Be(count);
    }

    cts.Cancel();

    Task.WaitAll(readers);
  }
}