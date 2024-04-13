using ConcurrencyToolkit.Metrics;
using FluentAssertions;

namespace ConcurrencyToolkit.Tests.Metrics;

public class ThreadSafeCounterTests
{
  [Test]
  public void SumDoublesSingleThread()
  {
    var counter = new ThreadSafeCounter64();

    counter.Add(1.1);
    counter.Add(1.2);
    counter.Add(1.3);
    counter.Add(1.4);
    counter.Add(1.5);

    const double target = 1.1 + 1.2 + 1.3 + 1.4 + 1.5;

    counter.GetDouble().Should().BeApproximately(target, 1e-9);
    counter.GetDoubleAndReset().Should().BeApproximately(target, 1e-9);
    counter.GetDouble().Should().Be(0);
    counter.GetDoubleAndReset().Should().Be(0);
  }

  [Test]
  public void SumLongsSingleThread()
  {
    var counter = new ThreadSafeCounter64();

    counter.Add(1);
    counter.Add(2);
    counter.Add(3);
    counter.Add(4);
    counter.Add(5);

    const int target = 1 + 2 + 3 + 4 + 5;

    counter.GetDouble().Should().BeApproximately(target, 1e-9);
    counter.GetDoubleAndReset().Should().BeApproximately(target, 1e-9);
    counter.GetDouble().Should().Be(0);
    counter.GetDoubleAndReset().Should().Be(0);
  }

  [Test]
  public void SumDoublesAndLongsSingleThread()
  {
    var counter = new ThreadSafeCounter64();

    counter.Add(1.1);
    counter.Add(1.2);
    counter.Add(1.3);
    counter.Add(1.4);
    counter.Add(1.5);
    counter.Add(1);
    counter.Add(2);
    counter.Add(3);
    counter.Add(4);
    counter.Add(5);

    const double target = 1.1 + 1.2 + 1.3 + 1.4 + 1.5 + 1 + 2 + 3 + 4 + 5;

    counter.GetDouble().Should().BeApproximately(target, 1e-9);
    counter.GetDoubleAndReset().Should().BeApproximately(target, 1e-9);
    counter.GetDouble().Should().Be(0);
    counter.GetDoubleAndReset().Should().Be(0);
  }
  [Test]
  public void Multithread()
  {
    var counter = new ThreadSafeCounter64();

    var tasks = new List<Task>();
    for (int i = 0; i < 4; i++)
    {
      var task = Task.Run(() =>
      {
        for (int j = 0; j < 100_000; j++)
        {
          counter.Add(Math.PI);
          counter.Add(2);
        }
      });

      tasks.Add(task);
    }

    Task.WhenAll(tasks).GetAwaiter().GetResult();

    counter.GetDouble().Should().BeApproximately(800_000 + 400_000 * Math.PI, 1e-9);

  }
}