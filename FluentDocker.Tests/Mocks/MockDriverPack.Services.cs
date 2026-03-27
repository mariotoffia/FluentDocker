using System;
using System.Collections.Generic;
using System.Threading;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using Moq;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Setup helpers for service-level unit tests (HostService, ImageService, PodService, EngineScope).
  /// </summary>
  public partial class MockDriverPack
  {
    #region System Driver Setups

    /// <summary>
    /// Sets up SystemDriver.GetInfoAsync to return success with the given info.
    /// </summary>
    public MockDriverPack SetupSystemInfo(SystemInfo info = null)
    {
      info ??= new SystemInfo();
      SystemDriver
          .Setup(d => d.GetInfoAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<SystemInfo>.Ok(info));
      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.GetInfoAsync to return failure.
    /// </summary>
    public MockDriverPack SetupSystemInfoFailure(string error = "system info failed")
    {
      SystemDriver
          .Setup(d => d.GetInfoAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<SystemInfo>.Fail(error));
      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.GetVersionAsync to return success.
    /// </summary>
    public MockDriverPack SetupSystemVersion(VersionInfo info = null)
    {
      info ??= new VersionInfo();
      SystemDriver
          .Setup(d => d.GetVersionAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<VersionInfo>.Ok(info));
      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.GetVersionAsync to return failure.
    /// </summary>
    public MockDriverPack SetupSystemVersionFailure(string error = "version failed")
    {
      SystemDriver
          .Setup(d => d.GetVersionAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<VersionInfo>.Fail(error));
      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.GetDiskUsageAsync to return success.
    /// </summary>
    public MockDriverPack SetupSystemDiskUsage(DiskUsageInfo info = null)
    {
      info ??= new DiskUsageInfo();
      SystemDriver
          .Setup(d => d.GetDiskUsageAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<DiskUsageInfo>.Ok(info));
      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.GetDiskUsageAsync to return failure.
    /// </summary>
    public MockDriverPack SetupSystemDiskUsageFailure(string error = "disk usage failed")
    {
      SystemDriver
          .Setup(d => d.GetDiskUsageAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<DiskUsageInfo>.Fail(error));
      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.PingAsync to return the given result.
    /// </summary>
    public MockDriverPack SetupSystemPing(bool success = true)
    {
      if (success)
      {
        SystemDriver
            .Setup(d => d.PingAsync(
                It.IsAny<DriverContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      }
      else
      {
        SystemDriver
            .Setup(d => d.PingAsync(
                It.IsAny<DriverContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CommandResponse<Unit>.Fail("ping failed"));
      }

      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.IsWindowsEngineAsync to return the given value.
    /// </summary>
    public MockDriverPack SetupSystemIsWindowsEngine(bool isWindows)
    {
      SystemDriver
          .Setup(d => d.IsWindowsEngineAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<bool>.Ok(isWindows));
      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.IsLinuxEngineAsync to return the given value.
    /// </summary>
    public MockDriverPack SetupSystemIsLinuxEngine(bool isLinux)
    {
      SystemDriver
          .Setup(d => d.IsLinuxEngineAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<bool>.Ok(isLinux));
      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.SwitchToLinuxDaemonAsync to return success.
    /// </summary>
    public MockDriverPack SetupSystemSwitchToLinux()
    {
      SystemDriver
          .Setup(d => d.SwitchToLinuxDaemonAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up SystemDriver.SwitchToWindowsDaemonAsync to return success.
    /// </summary>
    public MockDriverPack SetupSystemSwitchToWindows()
    {
      SystemDriver
          .Setup(d => d.SwitchToWindowsDaemonAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    #endregion

    #region Image Driver Additional Setups

    /// <summary>
    /// Sets up ImageDriver.HistoryAsync to return success.
    /// </summary>
    public MockDriverPack SetupImageHistory(IList<ImageLayer> layers = null)
    {
      layers ??= new List<ImageLayer>();
      ImageDriver
          .Setup(d => d.HistoryAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<IList<ImageLayer>>.Ok(layers));
      return this;
    }

    /// <summary>
    /// Sets up ImageDriver.TagAsync to return success.
    /// </summary>
    public MockDriverPack SetupImageTag()
    {
      ImageDriver
          .Setup(d => d.TagAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ImageDriver.PushAsync to return success.
    /// </summary>
    public MockDriverPack SetupImagePush()
    {
      ImageDriver
          .Setup(d => d.PushAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<IProgress<ImagePushProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ImageDriver.SaveAsync to return success.
    /// </summary>
    public MockDriverPack SetupImageSave()
    {
      ImageDriver
          .Setup(d => d.SaveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string[]>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    #endregion
  }
}
