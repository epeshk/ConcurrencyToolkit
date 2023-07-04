using System.Runtime.CompilerServices;

namespace ConcurrencyToolkit.Experiments.Internal;

internal static class ValueTaskEx
{
  public static ValueTask OmitResult<T>(ValueTask<T> task)
  {
    if (task.IsCompletedSuccessfully)
    {
      task.GetAwaiter().GetResult();
      return default;
    }

    return OmitResult2_Async(task);
  }

  [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
  public static async ValueTask OmitResult2_Async<T>(ValueTask<T> task) => await task;
}