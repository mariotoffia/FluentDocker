using System;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Testing.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FluentDocker.Testing.MsTest.RunnerTests
{
  /// <summary>
  /// Real-runner coverage for <see cref="MsTestAssemblyResource{TResource}"/> — the only
  /// supported way to share one container per MSTest assembly. Proves the documented
  /// <c>[AssemblyInitialize]</c>/<c>[AssemblyCleanup]</c> wiring works against the mock
  /// kernel, including the double-init guard and null-safe dispose (TSTX-4).
  /// </summary>
  [TestClass]
  public sealed class AssemblyResourceLifecycle
  {
    internal static readonly MsTestAssemblyResource<ContainerResource> Shared = new();
    internal static readonly MockContainerDriverPack Pack = new();

    [AssemblyInitialize]
    public static Task AssemblyInitialize(TestContext _) =>
        Shared.InitializeAsync(
            k => new ContainerResource(
                k,
                b => b.UseImage("alpine").WithName($"mstest-assembly-{Guid.NewGuid():N}"),
                new DockerResourceOptions
                {
                  Driver = DriverSelection.Specific("docker"),
                  ForceRemoveOnDispose = true
                }),
            async () => (await MockContainerKernel.CreateAsync(Pack).ConfigureAwait(false)).kernel);

    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
    {
      await Shared.DisposeAsync().ConfigureAwait(false);
      // Second dispose must be a null-safe no-op.
      await Shared.DisposeAsync().ConfigureAwait(false);
    }
  }

  [TestClass]
  [TestCategory("Unit")]
  public class AssemblyResourceRunnerTests
  {
    [TestMethod]
    public void AssemblyResource_IsInitialized_AndExposesResourceAndKernel()
    {
      Assert.IsNotNull(AssemblyResourceLifecycle.Shared.Resource);
      Assert.IsNotNull(AssemblyResourceLifecycle.Shared.Kernel);
      Assert.IsTrue(AssemblyResourceLifecycle.Shared.Resource.IsInitialized);
      Assert.AreEqual(1, AssemblyResourceLifecycle.Pack.CreateCalls);
    }

    [TestMethod]
    public async Task AssemblyResource_DoubleInitialize_Throws()
    {
      await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
          AssemblyResourceLifecycle.Shared.InitializeAsync(
              k => new ContainerResource(k, b => b.UseImage("alpine")))).ConfigureAwait(false);

      // The guard rejected before touching the driver: still exactly one create.
      Assert.AreEqual(1, AssemblyResourceLifecycle.Pack.CreateCalls);
    }

    [TestMethod]
    public async Task UninitializedAssemblyResource_AccessThrows_DisposeIsNullSafe()
    {
      var holder = new MsTestAssemblyResource<ContainerResource>();

      Assert.ThrowsException<InvalidOperationException>(() => _ = holder.Resource);
      Assert.ThrowsException<InvalidOperationException>(() => _ = holder.Kernel);

      // Never-initialized holder disposes cleanly (the documented AssemblyCleanup path when
      // AssemblyInitialize failed or never ran).
      await holder.DisposeAsync().ConfigureAwait(false);
    }
  }
}
