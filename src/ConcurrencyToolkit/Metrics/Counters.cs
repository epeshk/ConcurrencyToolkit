// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

namespace ConcurrencyToolkit.Metrics;

internal static class Counters
{
  private static Dictionary<Type, ThreadSafeCounter64> all = new();

  internal static void Register(Type type, ThreadSafeCounter64 counter)
  {
    lock (all)
      all.Add(type, counter);
  }

  internal static Dictionary<Type, long> GetAll()
  {
    lock (all)
    {
      var dict = new Dictionary<Type, long>(all.Count);
      foreach (var (key, value) in all)
        dict.Add(key, value.Get());
      return dict;
    }
  }

  internal static Dictionary<Type, long> GetAndResetAll()
  {
    lock (all)
    {
      var dict = new Dictionary<Type, long>(all.Count);
      foreach (var (key, value) in all)
        dict.Add(key, value.GetAndReset());
      return dict;
    }
  }
}