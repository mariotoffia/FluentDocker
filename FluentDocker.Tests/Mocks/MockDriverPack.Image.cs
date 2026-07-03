using System;
using System.Threading;
using FluentDocker.Drivers;
using Moq;
using DriverContext = FluentDocker.Model.Drivers.DriverContext;
using Unit = FluentDocker.Model.Drivers.Unit;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Image driver mock setup helpers for MockDriverPack.
  /// </summary>
  public partial class MockDriverPack
  {
    /// <summary>
    /// Sets up ImageDriver.PullAsync to return success.
    /// </summary>
    public MockDriverPack SetupImagePull()
    {
      ImageDriver
          .Setup(d => d.PullAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<string>(),
              It.IsAny<IProgress<ImagePullProgress>>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up ImageDriver.InspectAsync to return image details.
    /// </summary>
    public MockDriverPack SetupImageInspect(
        string imageId = "sha256:abc123",
        string? inspectedReference = null)
    {
      ImageDriver
          .Setup(d => d.InspectAsync(
              It.IsAny<DriverContext>(),
              It.Is<string>(value => inspectedReference == null || value == inspectedReference),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<Image>.Ok(new Image
          {
            Id = imageId
          }));
      return this;
    }

    /// <summary>
    /// Sets up ImageDriver.RemoveAsync to return success.
    /// </summary>
    public MockDriverPack SetupImageRemove()
    {
      ImageDriver
          .Setup(d => d.RemoveAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<bool>(),
              It.IsAny<bool>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(FluentDocker.Model.Drivers.CommandResponse<ImageRemoveResult>.Ok(
              new ImageRemoveResult()));
      return this;
    }
  }
}
