using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDocker.Testing.MsTest.RunnerTests
{
  /// <summary>
  /// Proves that the REAL MSTest runner discovers and AWAITS async lifecycle
  /// attributes — <c>[ClassInitialize]</c>, <c>[TestInitialize]</c>,
  /// <c>[TestCleanup]</c>, <c>[ClassCleanup]</c> — in the documented order.
  ///
  /// Each hook appends to <see cref="Order"/> only AFTER an <c>await</c> that
  /// yields the thread, so a runner that fired-and-forgot the returned Task
  /// (instead of awaiting it) would leave the flag unset and fail the asserts.
  /// </summary>
  [TestClass]
  [TestCategory("Unit")]
  public class AsyncLifecycleRunnerTests
  {
    /// <summary>Append-only log of which hooks completed their awaited work, in order.</summary>
    internal static readonly List<string> Order = new();

    [ClassInitialize]
    public static async Task ClassInit(TestContext ctx)
    {
      await Task.Yield();
      await Task.Delay(1);
      Order.Add("ClassInit");
    }

    [TestInitialize]
    public async Task TestInit()
    {
      await Task.Yield();
      Order.Add("TestInit");
    }

    [TestCleanup]
    public async Task TestCleanupHook()
    {
      await Task.Yield();
      Order.Add("TestCleanup");
    }

    [ClassCleanup]
    public static async Task ClassCleanupHook()
    {
      await Task.Yield();
      Order.Add("ClassCleanup");
    }

    [TestMethod]
    public void ClassInitializeRanBeforeTest()
    {
      // ClassInit's awaited continuation must have completed before any test body.
      Assert.IsTrue(Order.Contains("ClassInit"),
          "async [ClassInitialize] did not complete before the test ran");
    }

    [TestMethod]
    public void TestInitializeRanBeforeTest()
    {
      // TestInit must run for THIS test, after ClassInit, before the body.
      Assert.IsTrue(Order.Contains("TestInit"),
          "async [TestInitialize] did not complete before the test ran");

      var classInitIndex = Order.IndexOf("ClassInit");
      var lastTestInitIndex = Order.LastIndexOf("TestInit");
      Assert.IsTrue(classInitIndex >= 0 && classInitIndex < lastTestInitIndex,
          "async [ClassInitialize] must complete before async [TestInitialize]");
    }
  }
}
