using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace FluentDocker.Testing.NUnit.RunnerTests
{
  /// <summary>
  /// Proves the REAL NUnit runner discovers and awaits async lifecycle hooks.
  /// </summary>
  [TestFixture]
  [Category("Unit")]
  public class AsyncLifecycleRunnerTests
  {
    internal static readonly List<string> Order = new();

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
      await Task.Yield();
      await Task.Delay(1);
      Order.Add("OneTimeSetUp");
    }

    [SetUp]
    public async Task SetUp()
    {
      await Task.Yield();
      Order.Add("SetUp");
    }

    [TearDown]
    public async Task TearDown()
    {
      await Task.Yield();
      Order.Add("TearDown");
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
      await Task.Yield();
      Order.Add("OneTimeTearDown");
    }

    [Test]
    public void OneTimeSetUpRanBeforeTest()
    {
      Assert.That(Order, Does.Contain("OneTimeSetUp"),
          "async [OneTimeSetUp] did not complete before the test ran");
    }

    [Test]
    public void SetUpRanAfterOneTimeSetUp()
    {
      Assert.That(Order, Does.Contain("SetUp"),
          "async [SetUp] did not complete before the test ran");

      var oneTimeIndex = Order.IndexOf("OneTimeSetUp");
      var lastSetUpIndex = Order.LastIndexOf("SetUp");
      Assert.That(oneTimeIndex, Is.GreaterThanOrEqualTo(0));
      Assert.That(lastSetUpIndex, Is.GreaterThan(oneTimeIndex));
    }
  }
}
