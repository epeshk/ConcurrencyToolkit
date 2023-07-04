// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using ConcurrencyToolkit.Pooling.LiteObjectPool;

namespace ConcurrencyToolkit.Synchronization.Internal;

internal static class TaskExtensions
{
  public static T WaitSynchronously<T>(this Task<T> task)
  {
    var mre = ThreadStaticPool<ManualResetEventSlim>.TryRent();
    if (mre == null)
    {
      mre = new(false);
#pragma warning disable CA1816
      GC.SuppressFinalize(mre);
#pragma warning restore CA1816
    }

    task.ContinueWith(static (_, state) => ((ManualResetEventSlim)state!).Set(), mre, TaskContinuationOptions.ExecuteSynchronously);
    mre.Wait();
    mre.Reset();
    ThreadStaticPool<ManualResetEventSlim>.Return(mre);
    return task.GetAwaiter().GetResult();
  }

  public static void WaitSynchronously(this Task task)
  {
    var mre = ThreadStaticPool<ManualResetEventSlim>.TryRent();
    if (mre == null)
    {
      mre = new(false);
#pragma warning disable CA1816
      GC.SuppressFinalize(mre);
#pragma warning restore CA1816
    }

    task.ContinueWith(static (_, state) => ((ManualResetEventSlim)state!).Set(), mre, TaskContinuationOptions.ExecuteSynchronously);
    mre.Wait();
    mre.Reset();
    ThreadStaticPool<ManualResetEventSlim>.Return(mre);
    task.GetAwaiter().GetResult();
  }
}