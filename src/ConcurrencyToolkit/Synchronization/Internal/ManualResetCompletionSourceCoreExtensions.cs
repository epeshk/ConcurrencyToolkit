// This file is a part of the ConcurrencyToolkit library
// https://github.com/epeshk/ConcurrencyToolkit

using System.Threading.Tasks.Sources;
using ConcurrencyToolkit.Internal;
using ConcurrencyToolkit.Pooling.LiteObjectPool;
using ConcurrencyToolkit.Tasks.Sources;

namespace ConcurrencyToolkit.Synchronization.Internal;

internal static class ManualResetCompletionSourceCoreExtensions
{
  public static void WaitSynchronously<T>(ref this ManualResetCompletionSourceCore<T> core, short token)
  {
    var mre = ThreadStaticPool<ManualResetEventSlim>.TryRent();
    if (mre == null)
    {
      mre = new(false);
#pragma warning disable CA1816
      GC.SuppressFinalize(mre);
#pragma warning restore CA1816
    }

    // If OnCompleted method is called on an already completed ManualResetValueTaskSourceCore,
    // the callback will be queued to the thread pool, regardless of RunContinuationsAsynchronously setting
    //
    // In case of a thread pool starvation, there may be no available worker thread to signal the ManualResetEventSlim.
    // It is possible to stay with the BCL ManualResetValueTaskSourceCore, and not reinvent the wheel,
    // with synchronization context that executes this callback inline.
    //
    // However, SetResult method allocates SendOrPostCallback when SynchronizationContext is used.
    // This is why edited version of ManualResetValueTaskSourceCore is used.
    Assertion.False(core.RunContinuationsAsynchronously);
    core.OnCompleted(static o => ((ManualResetEventSlim)o!).Set(), mre, token, ValueTaskSourceOnCompletedFlags.None);
    mre.Wait();
    mre.Reset();
    ThreadStaticPool<ManualResetEventSlim>.Return(mre);
  }
}