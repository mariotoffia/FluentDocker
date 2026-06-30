using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Model.Models;
using FluentDocker.Testing.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace FluentDocker.Testing.MsTest.RunnerTests
{
  /// <summary>
  /// Drives a <see cref="ModelResource"/> through the real MSTest async lifecycle
  /// using <see cref="MsTestResourceHelpers"/>: provision in async <c>[TestInitialize]</c>,
  /// assert in <c>[TestMethod]</c>s, tear down in async <c>[TestCleanup]</c>.
  ///
  /// The cleanup itself <c>Verify</c>s that Unload ran, so a teardown that failed to
  /// unload would fail the test class — proving the runner awaits async cleanup.
  /// </summary>
  [TestClass]
  [TestCategory("Unit")]
  public class ModelResourceRunnerTests
  {
    private static readonly ModelReference Model = ModelReference.Parse("ai/smollm2:latest");

    private MockModelDriverPack _pack = null!;
    private FluentDockerKernel _kernel = null!;
    private ModelResource _resource = null!;

    [TestInitialize]
    public async Task TestInit()
    {
      (_kernel, _pack) = await MockModelKernel.CreateAsync();

      (_, _resource) = await MsTestResourceHelpers.CreateResourceAsync<ModelResource>(
          k => new ModelResource(k, Model),
          kernelFactory: () => Task.FromResult(_kernel));
    }

    [TestCleanup]
    public async Task TestCleanupHook()
    {
      if (_resource != null)
      {
        await MsTestResourceHelpers.DisposeAsync(_resource, _kernel);

        // A failed Verify throws here and fails the test, proving the async
        // teardown actually unloaded the model (and that the runner awaited it).
        _pack.RuntimeDriver.Verify(d => d.UnloadAsync(
            It.IsAny<DriverContext>(),
            It.Is<ModelReference>(m => m.Equals(Model)),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
      }
    }

    [TestMethod]
    public void ResourceIsInitialized()
    {
      Assert.IsTrue(_resource.IsInitialized,
          "async [TestInitialize] did not complete the ModelResource initialization");
    }

    [TestMethod]
    public void LoadWasInvokedDuringInitialize()
    {
      _pack.RuntimeDriver.Verify(d => d.LoadAsync(
          It.IsAny<DriverContext>(),
          It.Is<ModelReference>(m => m.Equals(Model)),
          It.IsAny<FluentDocker.Model.Models.Options.ModelRunOptions>(),
          It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public void RunnerMatchesServiceRunner()
    {
      Assert.AreSame(_resource.Service.Runner, _resource.Runner);
    }
  }
}
