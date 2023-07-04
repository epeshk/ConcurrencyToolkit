using ConcurrencyToolkit.Collections;
using FluentAssertions;

namespace ConcurrencyToolkit.Tests.Collections;

/// <summary>
/// Copied from https://github.com/dotnet/runtime/blob/812492e94de8dbe76ff1d6d13f298b010764adef/src/libraries/System.Collections.Concurrent/tests/StripedDictionary/StripedDictionaryTests.cs
/// </summary>
public class StripedDictionaryTests
{
  [Test]
  public static void TestBasicScenarios()
  {
    StripedDictionary<int, int, DefaultComparer<int>> cd = new ();

    Task[] tks = new Task[2];
    tks[0] = Task.Run(() =>
    {
      var ret = cd.TryAdd(1, 11);
      if (!ret)
      {
        ret = cd.TryUpdate(1, 11, 111);
        Assert.True(ret);
      }

      ret = cd.TryAdd(2, 22);
      if (!ret)
      {
        ret = cd.TryUpdate(2, 22, 222);
        Assert.True(ret);
      }
    });

    tks[1] = Task.Run(() =>
    {
      var ret = cd.TryAdd(2, 222);
      if (!ret)
      {
        ret = cd.TryUpdate(2, 222, 22);
        Assert.True(ret);
      }

      ret = cd.TryAdd(1, 111);
      if (!ret)
      {
        ret = cd.TryUpdate(1, 111, 11);
        Assert.True(ret);
      }
    });

    Task.WaitAll(tks);
  }

  [Test]
  public static void TestAddNullValue_StripedDictionaryOfString_null()
  {
    // using StripedDictionary<TKey, TValue> class
    StripedDictionary<string, string?, DefaultComparer<string>> dict1 = new ();
    dict1["key"] = null;
  }

  [Test]
  public static void TestAddNullValue_IDictionaryOfString_null()
  {
    // using IDictionary<TKey, TValue> interface
    IDictionary<string, string?> dict2 = new StripedDictionary<string, string?, DefaultComparer<string>>();
    dict2["key"] = null;
    dict2.Add("key2", null);
  }

  [TestCase(1, 1, 1, 10000)]
  [TestCase(5, 1, 1, 10000)]
  [TestCase(1, 1, 2, 5000)]
  [TestCase(1, 1, 5, 2000)]
  [TestCase(4, 0, 4, 2000)]
  [TestCase(16, 31, 4, 2000)]
  [TestCase(64, 5, 5, 5000)]
  [TestCase(5, 5, 5, 2500)]
  public void TestAdd(int cLevel, int initSize, int threads, int addsPerThread)
  {
    StripedDictionary<int, int, DefaultComparer<int>> dictConcurrent = new StripedDictionary<int, int, DefaultComparer<int>>(cLevel, 1);
    IDictionary<int, int> dict = dictConcurrent;

    int count = threads;
    using (ManualResetEvent mre = new ManualResetEvent(false))
    {
      for (int i = 0; i < threads; i++)
      {
        int ii = i;
        Task.Run(
          () =>
          {
            for (int j = 0; j < addsPerThread; j++)
            {
              dict.Add(j + ii * addsPerThread, -(j + ii * addsPerThread));
            }

            if (Interlocked.Decrement(ref count) == 0) mre.Set();
          });
      }

      mre.WaitOne();
    }

    foreach (var pair in dict)
    {
      pair.Key.Should().Be(-pair.Value);
    }

    List<int> gotKeys = new List<int>();
    foreach (var pair in dict)
      gotKeys.Add(pair.Key);

    gotKeys.Sort();

    List<int> expectKeys = new List<int>();
    int itemCount = threads * addsPerThread;
    for (int i = 0; i < itemCount; i++)
      expectKeys.Add(i);

    expectKeys.Count.Should().Be(gotKeys.Count);

    for (int i = 0; i < expectKeys.Count; i++)
    {
      Assert.True(expectKeys[i].Equals(gotKeys[i]),
        string.Format("The set of keys in the dictionary is are not the same as the expected" + Environment.NewLine +
                      "TestAdd1(cLevel={0}, initSize={1}, threads={2}, addsPerThread={3})", cLevel, initSize, threads,
          addsPerThread)
      );
    }

    // Finally, let's verify that the count is reported correctly.
    int expectedCount = threads * addsPerThread;
    dict.Count.Should().Be(expectedCount);
    dictConcurrent.ToArray().Length.Should().Be(expectedCount);
  }

  [TestCase(1, 1, 10000)]
  [TestCase(5, 1, 10000)]
  [TestCase(1, 2, 5000)]
  [TestCase(1, 5, 2001)]
  [TestCase(4, 4, 2001)]
  [TestCase(15, 5, 2001)]
  [TestCase(64, 5, 5000)]
  [TestCase(5, 5, 25000)]
  public static void TestUpdate(int cLevel, int threads, int updatesPerThread)
  {
    IDictionary<int, int> dict = new StripedDictionary<int,int,DefaultComparer<int>>(cLevel, 1);

    for (int i = 1; i <= updatesPerThread; i++) dict[i] = i;

    int running = threads;
    using (ManualResetEvent mre = new ManualResetEvent(false))
    {
      for (int i = 0; i < threads; i++)
      {
        int ii = i;
        Task.Run(
          () =>
          {
            for (int j = 1; j <= updatesPerThread; j++)
            {
              dict[j] = (ii + 2) * j;
            }

            if (Interlocked.Decrement(ref running) == 0) mre.Set();
          });
      }

      mre.WaitOne();
    }

    foreach (var pair in dict)
    {
      var div = pair.Value / pair.Key;
      var rem = pair.Value % pair.Key;

      rem.Should().Be(0);
      Assert.True(div > 1 && div <= threads + 1,
        string.Format("* Invalid value={3}! TestUpdate1(cLevel={0}, threads={1}, updatesPerThread={2})", cLevel,
          threads, updatesPerThread, div));
    }

    List<int> gotKeys = new List<int>();
    foreach (var pair in dict)
      gotKeys.Add(pair.Key);
    gotKeys.Sort();

    List<int> expectKeys = new List<int>();
    for (int i = 1; i <= updatesPerThread; i++)
      expectKeys.Add(i);

    gotKeys.Count.Should().Be(expectKeys.Count);

    for (int i = 0; i < expectKeys.Count; i++)
    {
      Assert.True(expectKeys[i].Equals(gotKeys[i]),
        string.Format("The set of keys in the dictionary is are not the same as the expected." + Environment.NewLine +
                      "TestUpdate1(cLevel={0}, threads={1}, updatesPerThread={2})", cLevel, threads, updatesPerThread)
      );
    }
  }

  [TestCase(1, 1, 10000)]
  [TestCase(5, 1, 10000)]
  [TestCase(1, 2, 5000)]
  [TestCase(1, 5, 2001)]
  [TestCase(4, 4, 2001)]
  [TestCase(15, 5, 2001)]
  [TestCase(64, 5, 5000)]
  [TestCase(5, 5, 25000)]
  public static void TestRead1(int cLevel, int threads, int readsPerThread)
  {
    IDictionary<int, int> dict = new StripedDictionary<int, int, DefaultComparer<int>>(cLevel, 1);

    for (int i = 0; i < readsPerThread; i += 2) dict[i] = i;

    int count = threads;
    using (ManualResetEvent mre = new ManualResetEvent(false))
    {
      for (int i = 0; i < threads; i++)
      {
        int ii = i;
        Task.Run(
          () =>
          {
            for (int j = 0; j < readsPerThread; j++)
            {
              int val = 0;
              if (dict.TryGetValue(j, out val))
              {
                (j % 2).Should().Be(0);
                val.Should().Be(j);
              }
              else
              {
                (j % 2).Should().Be(1);
              }
            }
            if (Interlocked.Decrement(ref count) == 0) mre.Set();
          });
      }
      mre.WaitOne();
    }
  }

  [TestCase(1, 1, 10000)]
  [TestCase(5, 1, 1000)]
  [TestCase(1, 5, 2001)]
  [TestCase(4, 4, 2001)]
  [TestCase(15, 5, 2001)]
  [TestCase(64, 5, 5000)]
  public static void TestRemove1(int cLevel, int threads, int removesPerThread)
  {
    StripedDictionary<int, int, DefaultComparer<int>> dict = new (cLevel, 1);
    string methodparameters = string.Format("* TestRemove1(cLevel={0}, threads={1}, removesPerThread={2})", cLevel,
      threads, removesPerThread);
    int N = 2 * threads * removesPerThread;

    for (int i = 0; i < N; i++) dict[i] = -i;

    // The dictionary contains keys [0..N), each key mapped to a value equal to the key.
    // Threads will cooperatively remove all even keys

    int running = threads;
    var tasks = new List<Task>();
    for (int i = 0; i < threads; i++)
    {
      int ii = i;
      var task = Task.Run(
        () =>
        {
          for (int j = 0; j < removesPerThread; j++)
          {
            int value;
            int key = 2 * (ii + j * threads);
            Assert.True(dict.TryRemove(key, out value), "Failed to remove an element! " + methodparameters);

            value.Should().Be(-key);
          }
        });

      tasks.Add(task);
    }

    Task.WhenAll(tasks).GetAwaiter().GetResult();

    foreach (var pair in dict)
    {
      Assert.AreEqual(pair.Key, -pair.Value);
    }

    List<int> gotKeys = new List<int>();
    foreach (var pair in dict)
      gotKeys.Add(pair.Key);
    gotKeys.Sort();

    List<int> expectKeys = new List<int>();
    for (int i = 0; i < (threads * removesPerThread); i++)
      expectKeys.Add(2 * i + 1);

    Assert.AreEqual(expectKeys.Count, gotKeys.Count);

    for (int i = 0; i < expectKeys.Count; i++)
    {
      Assert.True(expectKeys[i].Equals(gotKeys[i]), "  > Unexpected key value! " + methodparameters);
    }

    // Finally, let's verify that the count is reported correctly.
    Assert.AreEqual(expectKeys.Count, dict.Count);
    Assert.AreEqual(expectKeys.Count, dict.ToArray().Length);
  }

  [TestCase(1)]
  [TestCase(10)]
  [TestCase(5000)]
  public static void TestRemove2(int removesPerThread)
  {
    StripedDictionary<int, int, DefaultComparer<int>> dict = new ();

    for (int i = 0; i < removesPerThread; i++) dict[i] = -i;

    // The dictionary contains keys [0..N), each key mapped to a value equal to the key.
    // Threads will cooperatively remove all even keys.
    const int SIZE = 2;
    int running = SIZE;

    bool[][] seen = new bool[SIZE][];
    for (int i = 0; i < SIZE; i++) seen[i] = new bool[removesPerThread];

    using (ManualResetEvent mre = new ManualResetEvent(false))
    {
      for (int t = 0; t < SIZE; t++)
      {
        int thread = t;
        Task.Run(
          () =>
          {
            for (int key = 0; key < removesPerThread; key++)
            {
              int value;
              if (dict.TryRemove(key, out value))
              {
                seen[thread][key] = true;

                Assert.AreEqual(-key, value);
              }
            }
            if (Interlocked.Decrement(ref running) == 0) mre.Set();
          });
      }
      mre.WaitOne();
    }

    Assert.AreEqual(0, dict.Count);

    for (int i = 0; i < removesPerThread; i++)
    {
      Assert.False(seen[0][i] == seen[1][i],
        string.Format("> FAILED. Two threads appear to have removed the same element. TestRemove2(removesPerThread={0})", removesPerThread)
      );
    }
  }

  [Test]
  public static void TestRemove3()
  {
    StripedDictionary<int, int, DefaultComparer<int>> dict = new ();

    dict[99] = -99;

    ICollection<KeyValuePair<int, int>> col = dict;

    // Make sure we cannot "remove" a key/value pair which is not in the dictionary
    for (int i = 0; i < 200; i++)
    {
      if (i != 99)
      {
        Assert.False(col.Remove(new KeyValuePair<int, int>(i, -99)), "Should not remove not existing a key/value pair - new KeyValuePair<int, int>(i, -99)");
        Assert.False(col.Remove(new KeyValuePair<int, int>(99, -i)), "Should not remove not existing a key/value pair - new KeyValuePair<int, int>(99, -i)");
      }
    }

    // Can we remove a key/value pair successfully?
    Assert.True(col.Remove(new KeyValuePair<int, int>(99, -99)), "Failed to remove existing key/value pair");

    // Make sure the key/value pair is gone
    Assert.False(col.Remove(new KeyValuePair<int, int>(99, -99)), "Should not remove the key/value pair which has been removed");

    // And that the dictionary is empty. We will check the count in a few different ways:
    Assert.AreEqual(0, dict.Count);
    Assert.AreEqual(0, dict.ToArray().Length);
  }

  [Test]
  public static void TryRemove_KeyValuePair_ArgumentValidation()
  {
    Assert.Throws<ArgumentNullException>(() => new StripedDictionary<string, int, DefaultComparer<string>>().TryRemove(new KeyValuePair<string, int>(null, 42)));
    new StripedDictionary<int, int, DefaultComparer<int>>().TryRemove(new KeyValuePair<int, int>(0, 0)); // no error when using default value type
    new StripedDictionary<int?, int, DefaultComparer<int?>>().TryRemove(new KeyValuePair<int?, int>(0, 0)); // or nullable
  }

  [Test]
  public static void TryRemove_KeyValuePair_RemovesSuccessfullyAsAppropriate()
  {
    var dict = new StripedDictionary<string, int, DefaultComparer<string>>();

    for (int i = 0; i < 2; i++)
    {
      Assert.False(dict.TryRemove(KeyValuePair.Create("key", 42)));
      Assert.AreEqual(0, dict.Count);
      Assert.True(dict.TryAdd("key", 42));
      Assert.AreEqual(1, dict.Count);
      Assert.True(dict.TryRemove(KeyValuePair.Create("key", 42)));
      Assert.AreEqual(0, dict.Count);
    }

    Assert.True(dict.TryAdd("key", 42));
    Assert.False(dict.TryRemove(KeyValuePair.Create("key", 43))); // value doesn't match
  }

  [Test]
  public static void TryRemove_KeyValuePair_MatchesKeyWithDefaultComparer()
  {
    var dict = new StripedDictionary<string, string, ComparerWrapper<string>>(comparer: new(StringComparer.OrdinalIgnoreCase));
    dict.TryAdd("key", "value");
    Assert.False(dict.TryRemove(KeyValuePair.Create("key", "VALUE")));
    Assert.True(dict.TryRemove(KeyValuePair.Create("KEY", "value")));
  }

  [Test]
  public static void TestGetOrAdd()
  {
    TestGetOrAddOrUpdate(1, 1, 1, 10000, true);
    TestGetOrAddOrUpdate(5, 1, 1, 10000, true);
    TestGetOrAddOrUpdate(1, 1, 2, 5000, true);
    TestGetOrAddOrUpdate(1, 1, 5, 2000, true);
    TestGetOrAddOrUpdate(4, 0, 4, 2000, true);
    TestGetOrAddOrUpdate(16, 31, 4, 2000, true);
    TestGetOrAddOrUpdate(64, 5, 5, 5000, true);
    TestGetOrAddOrUpdate(5, 5, 5, 25000, true);
  }

  [Test]
  public static void TestAddOrUpdate()
  {
    TestGetOrAddOrUpdate(1, 1, 1, 10000, false);
    TestGetOrAddOrUpdate(5, 1, 1, 10000, false);
    TestGetOrAddOrUpdate(1, 1, 2, 5000, false);
    TestGetOrAddOrUpdate(1, 1, 5, 2000, false);
    TestGetOrAddOrUpdate(4, 0, 4, 2000, false);
    TestGetOrAddOrUpdate(16, 31, 4, 2000, false);
    TestGetOrAddOrUpdate(64, 5, 5, 5000, false);
    TestGetOrAddOrUpdate(5, 5, 5, 25000, false);
  }

  private static void TestGetOrAddOrUpdate(int cLevel, int initSize, int threads, int addsPerThread, bool isAdd)
  {
    StripedDictionary<int, int, DefaultComparer<int>> dict = new StripedDictionary<int, int, DefaultComparer<int>>(cLevel, 1);

    int count = threads;
    using (ManualResetEvent mre = new ManualResetEvent(false))
    {
      for (int i = 0; i < threads; i++)
      {
        int ii = i;
        Task.Run(
          () =>
          {
            for (int j = 0; j < addsPerThread; j++)
            {
              if (isAdd)
              {
                //call one of the overloads of GetOrAdd
                switch (j % 3)
                {
                  case 0:
                    dict.GetOrAdd(j, -j);
                    break;
                  case 1:
                    dict.GetOrAdd(j, x => -x);
                    break;
                  case 2:
                    dict.GetOrAdd(j, (x, m) => x * m, -1);
                    break;
                }
              }
              else
              {
                switch (j % 3)
                {
                  case 0:
                    dict.AddOrUpdate(j, -j, (k, v) => -j);
                    break;
                  case 1:
                    dict.AddOrUpdate(j, (k) => -k, (k, v) => -k);
                    break;
                  case 2:
                    dict.AddOrUpdate(j, (k, m) => k * m, (k, v, m) => k * m, -1);
                    break;
                }
              }
            }

            if (Interlocked.Decrement(ref count) == 0) mre.Set();
          });
      }

      mre.WaitOne();
    }

    foreach (var pair in dict)
    {
      Assert.AreEqual(pair.Key, -pair.Value);
    }

    List<int> gotKeys = new List<int>();
    foreach (var pair in dict)
      gotKeys.Add(pair.Key);
    gotKeys.Sort();

    List<int> expectKeys = new List<int>();
    for (int i = 0; i < addsPerThread; i++)
      expectKeys.Add(i);

    Assert.AreEqual(expectKeys.Count, gotKeys.Count);

    for (int i = 0; i < expectKeys.Count; i++)
    {
      Assert.True(expectKeys[i].Equals(gotKeys[i]),
        string.Format("* Test '{4}': Level={0}, initSize={1}, threads={2}, addsPerThread={3})" + Environment.NewLine +
                      "> FAILED.  The set of keys in the dictionary is are not the same as the expected.",
          cLevel, initSize, threads, addsPerThread, isAdd ? "GetOrAdd" : "GetOrUpdate"));
    }

    // Finally, let's verify that the count is reported correctly.
    Assert.AreEqual(addsPerThread, dict.Count);
    Assert.AreEqual(addsPerThread, dict.ToArray().Length);
  }

  [Test]
  public static void TestAddSyntax()
  {
    var dictionary = new StripedDictionary<int, int, DefaultComparer<int>> { {1, 1} };
    Assert.False(dictionary.IsEmpty);
    Assert.AreEqual(1, dictionary.Keys.Count);
    Assert.AreEqual(1, dictionary.Values.Count);
  }

  [Test]
  public static void TestExceptions()
  {
    var dictionary = new StripedDictionary<string, int, DefaultComparer<string>>();
    Assert.Throws<ArgumentNullException>(
      () => dictionary.TryAdd(null, 0));
    //  "TestExceptions:  FAILED.  TryAdd didn't throw ANE when null key is passed");

    Assert.Throws<ArgumentNullException>(
      () => dictionary.ContainsKey(null));
    // "TestExceptions:  FAILED.  Contains didn't throw ANE when null key is passed");

    int item;
    Assert.Throws<ArgumentNullException>(
      () => dictionary.TryRemove(null, out item));
    //  "TestExceptions:  FAILED.  TryRemove didn't throw ANE when null key is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.TryGetValue(null, out item));
    // "TestExceptions:  FAILED.  TryGetValue didn't throw ANE when null key is passed");

    Assert.Throws<ArgumentNullException>(
      () =>
      {
        var x = dictionary[null];
      });
    // "TestExceptions:  FAILED.  this[] didn't throw ANE when null key is passed");
    Assert.Throws<KeyNotFoundException>(
      () =>
      {
        var x = dictionary["1"];
      });
    // "TestExceptions:  FAILED.  this[] TryGetValue didn't throw KeyNotFoundException!");

    Assert.Throws<ArgumentNullException>(
      () => dictionary[null] = 1);
    // "TestExceptions:  FAILED.  this[] didn't throw ANE when null key is passed");

    Assert.Throws<ArgumentNullException>(
      () => dictionary.GetOrAdd(null, (k, m) => 0, 0));
    // "TestExceptions:  FAILED.  GetOrAdd didn't throw ANE when null key is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.GetOrAdd("1", null, 0));
    // "TestExceptions:  FAILED.  GetOrAdd didn't throw ANE when null valueFactory is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.GetOrAdd(null, (k) => 0));
    // "TestExceptions:  FAILED.  GetOrAdd didn't throw ANE when null key is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.GetOrAdd("1", null));
    // "TestExceptions:  FAILED.  GetOrAdd didn't throw ANE when null valueFactory is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.GetOrAdd(null, 0));
    // "TestExceptions:  FAILED.  GetOrAdd didn't throw ANE when null key is passed");

    Assert.Throws<ArgumentNullException>(
      () => dictionary.AddOrUpdate(null, (k, m) => 0, (k, v, m) => 0, 42));
    // "TestExceptions:  FAILED.  AddOrUpdate didn't throw ANE when null key is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.AddOrUpdate("1", (k, m) => 0, null, 42));
    // "TestExceptions:  FAILED.  AddOrUpdate didn't throw ANE when null updateFactory is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.AddOrUpdate("1", null, (k, v, m) => 0, 42));
    // "TestExceptions:  FAILED.  AddOrUpdate didn't throw ANE when null addFactory is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.AddOrUpdate(null, (k) => 0, (k, v) => 0));
    // "TestExceptions:  FAILED.  AddOrUpdate didn't throw ANE when null key is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.AddOrUpdate("1", null, (k, v) => 0));
    // "TestExceptions:  FAILED.  AddOrUpdate didn't throw ANE when null updateFactory is passed");
    Assert.Throws<ArgumentNullException>(
      () => dictionary.AddOrUpdate(null, (k) => 0, null));
    // "TestExceptions:  FAILED.  AddOrUpdate didn't throw ANE when null addFactory is passed");

    // Duplicate key.
    dictionary.TryAdd("1", 1);
    Assert.Throws<ArgumentException>(() => ((IDictionary<string, int>)dictionary).Add("1", 2));
  }

  [Test]
  public static void TestClear()
  {
    var dictionary = new StripedDictionary<int, int, DefaultComparer<int>>();
    for (int i = 0; i < 10; i++)
      dictionary.TryAdd(i, i);

    Assert.AreEqual(10, dictionary.Count);

    dictionary.Clear();
    Assert.AreEqual(0, dictionary.Count);

    int item;
    Assert.False(dictionary.TryRemove(1, out item), "TestClear: FAILED.  TryRemove succeeded after Clear");
    Assert.True(dictionary.IsEmpty, "TestClear: FAILED.  IsEmpty returned false after Clear");
  }

  [Test]
  public static void TestTryUpdate()
  {
    var dictionary = new StripedDictionary<string, int, DefaultComparer<string>>();
    Assert.Throws<ArgumentNullException>(
      () => dictionary.TryUpdate(null, 0, 0));
    // "TestTryUpdate:  FAILED.  TryUpdate didn't throw ANE when null key is passed");

    for (int i = 0; i < 10; i++)
      dictionary.TryAdd(i.ToString(), i);

    for (int i = 0; i < 10; i++)
    {
      Assert.True(dictionary.TryUpdate(i.ToString(), i + 1, i), "TestTryUpdate:  FAILED.  TryUpdate failed!");
      Assert.AreEqual(i + 1, dictionary[i.ToString()]);
    }

    //test TryUpdate concurrently
    dictionary.Clear();
    for (int i = 0; i < 1000; i++)
      dictionary.TryAdd(i.ToString(), i);

    var mres = new ManualResetEventSlim();
    Task[] tasks = new Task[10];
    ThreadLocal<ThreadData> updatedKeys = new ThreadLocal<ThreadData>(true);
    for (int i = 0; i < tasks.Length; i++)
    {
      // We are creating the Task using TaskCreationOptions.LongRunning because...
      // there is no guarantee that the Task will be created on another thread.
      // There is also no guarantee that using this TaskCreationOption will force
      // it to be run on another thread.
      tasks[i] = Task.Factory.StartNew((obj) =>
      {
        mres.Wait();
        int index = (((int)obj) + 1) + 1000;
        updatedKeys.Value = new ThreadData();
        updatedKeys.Value.ThreadIndex = index;

        for (int j = 0; j < dictionary.Count; j++)
        {
          if (dictionary.TryUpdate(j.ToString(), index, j))
          {
            if (dictionary[j.ToString()] != index)
            {
              updatedKeys.Value.Succeeded = false;
              return;
            }

            updatedKeys.Value.Keys.Add(j.ToString());
          }
        }
      }, i, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    mres.Set();
    Task.WaitAll(tasks);

    int numberSucceeded = 0;
    int totalKeysUpdated = 0;
    foreach (var threadData in updatedKeys.Values)
    {
      totalKeysUpdated += threadData.Keys.Count;
      if (threadData.Succeeded)
        numberSucceeded++;
    }

    Assert.True(numberSucceeded == tasks.Length, "One or more threads failed!");
    Assert.True(totalKeysUpdated == dictionary.Count,
      string.Format(
        "TestTryUpdate:  FAILED.  The updated keys count doesn't match the dictionary count, expected {0}, actual {1}",
        dictionary.Count, totalKeysUpdated));
    foreach (var value in updatedKeys.Values)
    {
      for (int i = 0; i < value.Keys.Count; i++)
        Assert.True(dictionary[value.Keys[i]] == value.ThreadIndex,
          string.Format(
            "TestTryUpdate:  FAILED.  The updated value doesn't match the thread index, expected {0} actual {1}",
            value.ThreadIndex, dictionary[value.Keys[i]]));
    }

    //test TryUpdate with non atomic values (intPtr > 8)
    var dict = new StripedDictionary<int, Struct16, DefaultComparer<int>>();
    dict.TryAdd(1, new Struct16(1, -1));
    Assert.True(dict.TryUpdate(1, new Struct16(2, -2), new Struct16(1, -1)),
      "TestTryUpdate:  FAILED.  TryUpdate failed for non atomic values ( > 8 bytes)");
  }

  [Test]
  public void ConcurrentWriteRead_NoTornValues()
  {
    var cd = new StripedDictionary<int, KeyValuePair<long, long>, DefaultComparer<int>>();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    Task.WaitAll(
      Task.Run(() =>
      {
        for (long i = 0; !cts.IsCancellationRequested; i++)
        {
          cd[0] = new KeyValuePair<long, long>(i, i);
        }
      }),
      Task.Run(() =>
      {
        while (!cts.IsCancellationRequested)
        {
          cd.TryGetValue(0, out KeyValuePair<long, long> item);
          try
          {
            Assert.AreEqual(item.Key, item.Value);
          }
          catch
          {
            cts.Cancel();
            throw;
          }
        }
      }));
  }

  private record struct Struct16(long a, long b);
  private class ThreadData
  {
    public int ThreadIndex;
    public bool Succeeded = true;
    public List<string> Keys = new List<string>();
  }
}