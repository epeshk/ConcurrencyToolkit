using System.Runtime.CompilerServices;
using ConcurrencyToolkit.Pooling;
using FluentAssertions;

namespace ConcurrencyToolkit.Tests;

public class LockFreeObjectPoolTests
{
  private LocklessObjectPool<StrongBox<int>> pool;

  [SetUp]
  public void Setup()
  {
    pool = new LocklessObjectPool<StrongBox<int>>(() => new StrongBox<int>());
  }

  [Test]
  public void ShouldReuseObjects()
  {
    var o1 = pool.Rent();
    var o2 = pool.Rent();
    var o3 = pool.Rent();
    pool.Return(o1);
    pool.Return(o2);
    pool.Return(o3);
    var o4 = pool.Rent();
    var o5 = pool.Rent();
    var o6 = pool.Rent();

    new[] { o1, o2, o3 }.Should().BeSubsetOf(new[] { o4, o5, o6 });
  }

  [Test]
  public void ShouldReuseObjectsFromDifferentThreads()
  {
    StrongBox<int> Rent() => Task.Run(() => pool.Rent()).GetAwaiter().GetResult();
    void Return(StrongBox<int> obj) => Task.Run(() => pool.Return(obj)).GetAwaiter().GetResult();

    var o1 = Rent();
    var o2 = Rent();
    var o3 = Rent();
    Return(o1);
    Return(o2);
    Return(o3);
    var o4 = Rent();
    var o5 = Rent();
    var o6 = Rent();

    new[] { o1, o2, o3 }.Should().BeEquivalentTo(new[] { o4, o5, o6 });
  }

  [Test]
  public void ShouldCreateNewObjects_WhenNothingToReuse()
  {
    var o1 = pool.Rent();
    var o2 = pool.Rent();
    var o3 = pool.Rent();
    var o4 = pool.Rent();
    var o5 = pool.Rent();
    var o6 = pool.Rent();

    new[] { o1, o2, o3, o4, o5, o6 }.Distinct().Should().HaveCount(6);
  }
}