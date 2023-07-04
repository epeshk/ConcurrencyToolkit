using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ConcurrencyToolkit.Collections;

namespace ConcurrencyToolkit.Benchmarks.Collections;

[SimpleJob(RuntimeMoniker.Net80)]
// [SimpleJob(RuntimeMoniker.NativeAot80)]
[MemoryDiagnoser]
[WarmupCount(32)]
[MaxIterationCount(32)]
public class SingleWriterDictionaryBenchmarks
{
  private const int ElementCount = 16*1024;
  private const int ActiveElements = 128;
  private const int T_W = 1;
  private const int T_R = 2;
  private const int Rounds = 4096*8;

  private readonly Guid[] keys = new Guid[ElementCount];
  private readonly Guid[] value = new Guid[ElementCount];

  private readonly List<Guid[]> keys_shuffled_write = new ();
  private readonly List<Guid[]> keys_shuffled_read = new ();

  private readonly SingleWriterDictionary<Guid, Guid, DefaultComparer<Guid>> sw = new();
  private readonly ConcurrentDictionary<Guid, Guid> cd = new();
  private readonly StripedDictionary<Guid, Guid, DefaultComparer<Guid>> stripedDictionary = new();
  private readonly Dictionary<Guid, Guid> nonconcurrent = new();
  private readonly Hashtable ht = new();

  private readonly Guid[] allowedValues = new[] { Guid.NewGuid(), Guid.NewGuid() };

  [GlobalSetup]
  public void Setup()
  {
    var random = new Random(1);
    Span<byte> guid = stackalloc byte[Unsafe.SizeOf<Guid>()];
    for (int i = 0; i < ElementCount; i++)
    {
      random.NextBytes(guid);
      keys[i] = new Guid(guid);
      value[i] = allowedValues[random.Next(0, allowedValues.Length)];
    }

    var keys_ = this.keys.OrderBy(x => random.Next()).Take(ActiveElements).ToArray();

    for (int i = 0; i < T_W; i++)
    {
      var arr = keys_.OrderBy(x => random.Next()).ToArray();
      keys_shuffled_write.Add(arr);
    }

    var kkeys = keys_.Concat(Enumerable.Range(0, keys_.Length).Select(_ =>
    {
      Span<byte> guid = stackalloc byte[Unsafe.SizeOf<Guid>()];
      random.NextBytes(guid);
      return new Guid(guid);
    })).ToArray();

    for (int i = 0; i < T_R; i++)
    {
      var arr = kkeys.OrderBy(x => random.Next()).ToArray();
      keys_shuffled_read.Add(arr);
    }

    foreach (var k in keys)
      ht[k] = stripedDictionary[k] = nonconcurrent[k] = sw.Writer[k] = cd[k] = allowedValues[random.Next(0, allowedValues.Length)];
  }

  [Benchmark]
  public void Striped_WritingTime() => WritingTime(stripedDictionary, stripedDictionary);

  [Benchmark]
  public void SingleWriter_WritingTime() => WritingTime(sw.Writer, sw.Reader);

  [Benchmark]
  public void Concurrent_WritingTime() => WritingTime(cd, cd);

  [Benchmark]
  public void Striped_ReadingTime() => ReadingTime(stripedDictionary, stripedDictionary);

  [Benchmark]
  public void SingleWriter_ReadingTime() => ReadingTime(sw.Writer, sw.Reader);

  [Benchmark]
  public void Concurrent_ReadingTime() => ReadingTime(cd, cd);

  [Benchmark]
  public void Striped_UncontentedReads() => UncontentedReads(stripedDictionary);

  [Benchmark]
  public void SingleWriter_UncontentedReads_Read() => UncontentedReads(sw.Reader);

  [Benchmark]
  public void SingleWriter_UncontentedReads_Write() => UncontentedReads(sw.Writer);

  [Benchmark]
  public void Concurrent_UncontentedReads() => UncontentedReads(cd);

  [Benchmark]
  public void Simple_UncontentedReads() => UncontentedReads(nonconcurrent);

  [Benchmark]
  public void Striped_UncontentedWrites() => UncontentedWrites(stripedDictionary);

  [Benchmark]
  public void SingleWriter_UncontentedWrites() => UncontentedWrites(sw.Writer);

  [Benchmark]
  public void Concurrent_UncontentedWrites() => UncontentedWrites(cd);

  [Benchmark]
  public void Simple_UncontentedWrites() => UncontentedWrites(nonconcurrent);

  [Benchmark]
  public void WritingTime_Locked()
  {
    var dict = this.nonconcurrent;
    var cts = new CancellationTokenSource();
    var cancellation = cts.Token;

    var tasks = keys_shuffled_read.Select(keys => Task.Run(() =>
    {
      int k = 0;
      while (true)
      {
        foreach (var key in keys)
        {
          if (k++ % 100 == 0 && cancellation.IsCancellationRequested) return;

          lock (dict)
          {
            if (dict.TryGetValue(key, out var val) && !allowedValues.Contains(val))
              throw new();
          }
        }
      }
    })).ToArray();

    for (var i = 0; i < Rounds; ++i)
    {
      uint j = 0;
      foreach (var guid in keys_shuffled_write[0])
      {
        lock (dict)
          dict[guid] = allowedValues[j++ % allowedValues.Length];
      }
    }

    cts.Cancel();
    Task.WaitAll(tasks);
  }

  [Benchmark]
  public void WritingTime_HT()
  {
    var dict = this.ht;
    var cts = new CancellationTokenSource();
    var cancellation = cts.Token;

    var tasks = keys_shuffled_read.Select(keys => Task.Run(() =>
    {
      int k = 0;
      while (true)
      {
        foreach (var key in keys)
        {
          if (k++ % 100 == 0 && cancellation.IsCancellationRequested) return;

          lock (dict)
          {
            if (dict.ContainsKey(key) && !allowedValues.Contains((Guid)dict[key]))
              throw new();
          }
        }
      }
    })).ToArray();

    for (var i = 0; i < Rounds; ++i)
    {
      uint j = 0;
      foreach (var guid in keys_shuffled_write[0])
      {
        lock (dict)
          dict[guid] = allowedValues[j++ % allowedValues.Length];
      }
    }

    cts.Cancel();
    Task.WaitAll(tasks);
  }

  private void WritingTime(IDictionary<Guid, Guid> writer, IReadOnlyDictionary<Guid, Guid> reader)
  {
    var cts = new CancellationTokenSource();
    var cancellation = cts.Token;

    var tasks = keys_shuffled_read.Select(keys => Task.Run(() =>
    {
      int k = 0;
      while (true)
      {
        foreach (var key in keys)
        {
          if (k++ % 100 == 0 && cancellation.IsCancellationRequested) return;

          if (reader.TryGetValue(key, out var val) && !allowedValues.Contains(val))
            throw new();
        }
      }
    })).ToArray();

    uint j = 0;
    for (var i = 0; i < Rounds; ++i)
    {
      foreach (var guid in keys_shuffled_write[0])
      {
        writer[guid] = allowedValues[j];
        if (++j == allowedValues.Length)
          j = 0;
      }
    }

    cts.Cancel();
    Task.WaitAll(tasks);
  }

  private void ReadingTime(IDictionary<Guid, Guid> w, IReadOnlyDictionary<Guid, Guid> r)
  {
    var readers = new List<Task>();

    readers.AddRange(Enumerable.Range(0, T_R).Select(x => Task.Run(() =>
    {
      var keys = keys_shuffled_read[x];
      for (int i = 0; i < Rounds; i++)
      {
        foreach (var key in keys)
        {
          if (r.TryGetValue(key, out var val) && !allowedValues.Contains(val))
            throw new();
        }
      }
    })));

    var allReaders = Task.WhenAll(readers);

    Task writer = Task.Run(() =>
    {
      var keys = keys_shuffled_write[0];
      while (true)
      {
        uint k = 0;
        uint j = 0;
        foreach (var key in keys)
        {
          if (k++ % 100 == 0 && allReaders.IsCompleted) return;
          w[key] = allowedValues[j];
          if (++j == allowedValues.Length)
            j = 0;
        }
      }
    });

    writer.GetAwaiter().GetResult();
  }

  private void UncontentedReads(IReadOnlyDictionary<Guid, Guid> dict)
  {
    for (var i = 0; i < Rounds; ++i)
    {
      foreach (var guid in keys_shuffled_read[0])
      {
        dict.TryGetValue(guid, out var val);
        if (!allowedValues.Contains(val))
          throw new();
      }
    }
  }

  private void UncontentedWrites(IDictionary<Guid, Guid> dict)
  {
    for (var i = 0; i < Rounds; ++i)
    {
      uint j = 0;
      foreach (var guid in keys_shuffled_write[0])
      {
        dict[guid] = allowedValues[j++ % allowedValues.Length];
      }
    }
  }
}