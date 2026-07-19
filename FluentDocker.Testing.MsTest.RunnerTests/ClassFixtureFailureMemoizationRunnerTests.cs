using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Testing.MsTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDocker.Testing.MsTest.RunnerTests
{
  /// <summary>
  /// Proves the class-fixture base memoizes the first initialization failure instead of
  /// re-running full container init (kernel build + provision) for every test method — the
  /// behavior that let one dead daemon burn minutes of CI per class (Chunk 4 C-2). The helper
  /// fixtures below are NOT <c>[TestClass]</c>; they are driven by hand so the runner never
  /// executes their deliberately failing init.
  /// </summary>
  [TestClass]
  [TestCategory("Unit")]
  public class ClassFixtureFailureMemoizationRunnerTests
  {
    private static Func<Task<FluentDockerKernel>> FailingKernelFactory(Action onCall) =>
        () =>
        {
          onCall();
          return Task.FromException<FluentDockerKernel>(new InvalidOperationException("init boom"));
        };

    private sealed class MemoizingFailingFixture : MsTestClassContainerFixtureBase<MemoizingFailingFixture>
    {
      public static int KernelFactoryCalls;

      protected override void ConfigureContainer(IContainerBuilder builder) =>
          builder.UseImage("alpine:latest");

      protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
          FailingKernelFactory(() => Interlocked.Increment(ref KernelFactoryCalls));

      public static Task ResetAsync() => CleanupClassAsync();
    }

    private sealed class RetryingFailingFixture : MsTestClassContainerFixtureBase<RetryingFailingFixture>
    {
      public static int KernelFactoryCalls;

      protected override bool RetryInitializationPerTest => true;

      protected override void ConfigureContainer(IContainerBuilder builder) =>
          builder.UseImage("alpine:latest");

      protected override Func<Task<FluentDockerKernel>>? KernelFactory =>
          FailingKernelFactory(() => Interlocked.Increment(ref KernelFactoryCalls));

      public static Task ResetAsync() => CleanupClassAsync();
    }

    [TestMethod]
    public async Task ClassInitFailure_IsMemoized_NotReattemptedPerTest()
    {
      await MemoizingFailingFixture.ResetAsync();
      MemoizingFailingFixture.KernelFactoryCalls = 0;
      var fixture = new MemoizingFailingFixture();

      await AssertThrowsAsync(() => fixture.TestInitializeAsync());
      Assert.AreEqual(1, MemoizingFailingFixture.KernelFactoryCalls);

      // Subsequent "test methods" must replay the memoized failure, not re-initialize.
      await AssertThrowsAsync(() => fixture.TestInitializeAsync());
      await AssertThrowsAsync(() => fixture.TestInitializeAsync());
      Assert.AreEqual(1, MemoizingFailingFixture.KernelFactoryCalls);

      await MemoizingFailingFixture.ResetAsync();
    }

    [TestMethod]
    public async Task RetryKnob_ReattemptsInitializationPerTest()
    {
      await RetryingFailingFixture.ResetAsync();
      RetryingFailingFixture.KernelFactoryCalls = 0;
      var fixture = new RetryingFailingFixture();

      await AssertThrowsAsync(() => fixture.TestInitializeAsync());
      await AssertThrowsAsync(() => fixture.TestInitializeAsync());

      // With the opt-in knob, each test method re-attempts init (pre-3.2 behavior).
      Assert.AreEqual(2, RetryingFailingFixture.KernelFactoryCalls);

      await RetryingFailingFixture.ResetAsync();
    }

    private static async Task AssertThrowsAsync(Func<Task> action)
    {
      try
      {
        await action();
      }
      catch
      {
        return;
      }

      Assert.Fail("Expected initialization to throw.");
    }
  }
}
