using System;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using FluentDocker.Services;
using FluentDocker.Services.Impl;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Service
{
  /// <summary>
  /// Unit tests for <see cref="EngineScope"/>.
  /// </summary>
  [Trait("Category", "Unit")]
  public class EngineScopeTests
  {
    private const string DriverId = "docker";

    /// <summary>
    /// Helper to set up mock + kernel with engine detection configured.
    /// </summary>
    private static async Task<(FluentDockerKernel kernel, MockDriverPack mockPack)> CreateKernelAsync(
        bool isWindowsEngine)
    {
      var mockPack = new MockDriverPack();
      mockPack.SetupSystemIsWindowsEngine(isWindowsEngine);
      mockPack.SetupSystemSwitchToLinux();
      mockPack.SetupSystemSwitchToWindows();
      var kernel = await MockKernelBuilderExtensions.CreateWithMockDriverAsync(DriverId, mockPack);
      return (kernel, mockPack);
    }

    #region Factory / Detection Tests

    [Fact]
    public async Task CreateAsync_DetectsLinuxScope_SetsScope()
    {
      // Arrange: engine reports NOT windows => Linux detected
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      try
      {
        // Act: target Linux — same as detected, no switch needed
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Linux, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EngineScopeType.Linux, scope.Scope);
        mockPack.SystemDriver.Verify(
            d => d.SwitchToLinuxDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());
        mockPack.SystemDriver.Verify(
            d => d.SwitchToWindowsDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task CreateAsync_DetectsWindowsScope_SetsScope()
    {
      // Arrange: engine reports windows => Windows detected
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: true);
      try
      {
        // Act: target Windows — same as detected, no switch needed
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Windows, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EngineScopeType.Windows, scope.Scope);
        mockPack.SystemDriver.Verify(
            d => d.SwitchToWindowsDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());
        mockPack.SystemDriver.Verify(
            d => d.SwitchToLinuxDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task CreateAsync_TargetDiffersFromCurrent_SwitchesScope()
    {
      // Arrange: engine reports NOT windows => Linux detected, target Windows
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      try
      {
        // Act
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Windows, TestContext.Current.CancellationToken);

        // Assert: should have switched to Windows
        Assert.Equal(EngineScopeType.Windows, scope.Scope);
        mockPack.SystemDriver.Verify(
            d => d.SwitchToWindowsDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Once());

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task CreateAsync_TargetSameAsCurrent_DoesNotSwitch()
    {
      // Arrange: engine reports NOT windows => Linux detected, target Linux
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      try
      {
        // Act
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Linux, TestContext.Current.CancellationToken);

        // Assert: no switch should be called
        Assert.Equal(EngineScopeType.Linux, scope.Scope);
        mockPack.SystemDriver.Verify(
            d => d.SwitchToLinuxDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());
        mockPack.SystemDriver.Verify(
            d => d.SwitchToWindowsDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task CreateAsync_TargetUnknown_DoesNotSwitch()
    {
      // Arrange: engine reports NOT windows => Linux detected, target Unknown
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      try
      {
        // Act
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Unknown, TestContext.Current.CancellationToken);

        // Assert: no switch should be called when target is Unknown
        Assert.Equal(EngineScopeType.Linux, scope.Scope);
        mockPack.SystemDriver.Verify(
            d => d.SwitchToLinuxDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());
        mockPack.SystemDriver.Verify(
            d => d.SwitchToWindowsDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion

    #region Query Method Tests

    [Fact]
    public async Task IsWindowsEngineAsync_ReturnsDriverResult()
    {
      // Arrange: engine reports windows
      var (kernel, _) = await CreateKernelAsync(isWindowsEngine: true);
      try
      {
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Windows, TestContext.Current.CancellationToken);

        // Act
        var result = await scope.IsWindowsEngineAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task IsLinuxEngineAsync_ReturnsDriverResult()
    {
      // Arrange: engine reports NOT windows => Linux; also set up IsLinuxEngine
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      mockPack.SetupSystemIsLinuxEngine(true);
      try
      {
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Linux, TestContext.Current.CancellationToken);

        // Act
        var result = await scope.IsLinuxEngineAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion

    #region Use Method Tests

    [Fact]
    public async Task UseLinuxAsync_AlreadyLinux_ReturnsTrueWithoutCalling()
    {
      // Arrange: detected Linux, target Linux
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      try
      {
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Linux, TestContext.Current.CancellationToken);

        // Act
        var result = await scope.UseLinuxAsync(TestContext.Current.CancellationToken);

        // Assert: already Linux, so SwitchToLinux should NOT be called
        Assert.True(result);
        mockPack.SystemDriver.Verify(
            d => d.SwitchToLinuxDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task UseLinuxAsync_FromWindows_SwitchesAndReturnsTrue()
    {
      // Arrange: detected Windows, target Windows (no switch in factory)
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: true);
      try
      {
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Windows, TestContext.Current.CancellationToken);

        // Act: explicitly switch to Linux
        var result = await scope.UseLinuxAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal(EngineScopeType.Linux, scope.Scope);
        mockPack.SystemDriver.Verify(
            d => d.SwitchToLinuxDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Once());

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task UseWindowsAsync_AlreadyWindows_ReturnsTrueWithoutCalling()
    {
      // Arrange: detected Windows, target Windows
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: true);
      try
      {
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Windows, TestContext.Current.CancellationToken);

        // Act
        var result = await scope.UseWindowsAsync(TestContext.Current.CancellationToken);

        // Assert: already Windows, so SwitchToWindows should NOT be called
        Assert.True(result);
        mockPack.SystemDriver.Verify(
            d => d.SwitchToWindowsDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task UseWindowsAsync_FromLinux_SwitchesAndReturnsTrue()
    {
      // Arrange: detected Linux, target Linux (no switch in factory)
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      try
      {
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Linux, TestContext.Current.CancellationToken);

        // Act: explicitly switch to Windows
        var result = await scope.UseWindowsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.Equal(EngineScopeType.Windows, scope.Scope);
        mockPack.SystemDriver.Verify(
            d => d.SwitchToWindowsDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Once());

        await scope.DisposeAsync();
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public async Task DisposeAsync_RestoresOriginalScope()
    {
      // Arrange: detect Linux (IsWindowsEngine=false), then switch to Windows
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      try
      {
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Linux, TestContext.Current.CancellationToken);

        // Switch away from original scope
        await scope.UseWindowsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(EngineScopeType.Windows, scope.Scope);

        // Reset mock invocations so we can verify only the restore call
        mockPack.SystemDriver.Invocations.Clear();

        // Act: dispose should restore to original Linux scope
        await scope.DisposeAsync();

        // Assert: SwitchToLinux should be called to restore
        mockPack.SystemDriver.Verify(
            d => d.SwitchToLinuxDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Once());
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task DisposeAsync_SameScope_DoesNotRestore()
    {
      // Arrange: detect Linux, target Linux — scope never changes
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      try
      {
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Linux, TestContext.Current.CancellationToken);

        // Reset mock invocations
        mockPack.SystemDriver.Invocations.Clear();

        // Act
        await scope.DisposeAsync();

        // Assert: no switch calls during dispose
        mockPack.SystemDriver.Verify(
            d => d.SwitchToLinuxDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());
        mockPack.SystemDriver.Verify(
            d => d.SwitchToWindowsDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Never());
      }
      finally
      {
        kernel.Dispose();
      }
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_SecondIsNoop()
    {
      // Arrange: detect Linux, switch to Windows so dispose will restore
      var (kernel, mockPack) = await CreateKernelAsync(isWindowsEngine: false);
      try
      {
        var scope = await EngineScope.CreateAsync(kernel, DriverId, EngineScopeType.Linux, TestContext.Current.CancellationToken);

        // Switch away from original scope
        await scope.UseWindowsAsync(TestContext.Current.CancellationToken);

        // Reset mock invocations
        mockPack.SystemDriver.Invocations.Clear();

        // Act: dispose twice
        await scope.DisposeAsync();
        await scope.DisposeAsync();

        // Assert: SwitchToLinux should only be called once (second dispose is a no-op)
        mockPack.SystemDriver.Verify(
            d => d.SwitchToLinuxDaemonAsync(
                It.IsAny<DriverContext>(), It.IsAny<CancellationToken>()),
            Times.Once());
      }
      finally
      {
        kernel.Dispose();
      }
    }

    #endregion
  }
}
