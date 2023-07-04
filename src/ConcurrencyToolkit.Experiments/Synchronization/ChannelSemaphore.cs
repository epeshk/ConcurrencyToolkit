using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using ConcurrencyToolkit.Experiments.Internal;
using ConcurrencyToolkit.Synchronization;

namespace ConcurrencyToolkit.Experiments.Synchronization;

public class ChannelSemaphore : ISemaphore
{
  [StructLayout(LayoutKind.Explicit, Size = 0)]
  private struct Unit { }

  private readonly Channel<Unit> channel;

  public ChannelSemaphore(int initialCount)
  {
    channel = Channel.CreateBounded<Unit>(new BoundedChannelOptions(initialCount)
    {
      AllowSynchronousContinuations = false
    });

    while (initialCount-- > 0)
      channel.Writer.TryWrite(default);
  }

  public ValueTask AcquireAsync(CancellationToken cancellationToken = default) =>
    ValueTaskEx.OmitResult(channel.Reader.ReadAsync(cancellationToken));

  [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
  public ValueTask<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
  {
    var task = channel.Reader.ReadAsync(cancellationToken).AsTask().ContinueWith(
      static t => t.IsCompletedSuccessfully,
      TaskContinuationOptions.ExecuteSynchronously);

    return new(task);
  }

  public void Acquire(CancellationToken cancellationToken = default) =>
    channel.Reader.ReadAsync(cancellationToken).AsTask().GetAwaiter().GetResult();

  public bool TryAcquire(CancellationToken cancellationToken = default) =>
    TryAcquireAsync(cancellationToken).AsTask().GetAwaiter().GetResult();

  public bool TryAcquireImmediately() =>
    channel.Reader.TryRead(out _);

  public void Release() =>
    channel.Writer.TryWrite(default);

  public int CurrentCount => channel.Reader.Count;
  public int CurrentQueue { get; }
}