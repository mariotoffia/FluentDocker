using System.Collections.Generic;
using System.Threading;
using FluentDocker.Drivers.Podman;
using FluentDocker.Model.Drivers;
using Moq;

namespace FluentDocker.Tests.Mocks
{
  /// <summary>
  /// Podman Kubernetes mock setup helpers for MockDriverPack.
  /// </summary>
  public partial class MockDriverPack
  {
    /// <summary>
    /// Mock Podman Kubernetes driver.
    /// </summary>
    public Mock<IPodmanKubernetesDriver> PodmanKubernetesDriver { get; }
        = new Mock<IPodmanKubernetesDriver>();

    /// <summary>
    /// Registers and sets up the IPodmanKubernetesDriver for testing.
    /// Call this before tests that need Kubernetes support.
    /// </summary>
    public MockDriverPack EnablePodmanKubernetesDriver()
    {
      _drivers[typeof(IPodmanKubernetesDriver)] = PodmanKubernetesDriver.Object;
      return this;
    }

    /// <summary>
    /// Sets up PodmanKubernetesDriver.PlayAsync to return success.
    /// </summary>
    public MockDriverPack SetupKubePlay(string yamlPath = "test.yaml")
    {
      PodmanKubernetesDriver
          .Setup(d => d.PlayAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<KubePlayConfig>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<KubePlayResult>.Ok(
              new KubePlayResult
              {
                Pods = new List<KubePlayPodResult>
                {
                  new KubePlayPodResult
                  {
                    Id = "pod-123",
                    Containers = new List<string> { "container-456" }
                  }
                }
              }));
      return this;
    }

    /// <summary>
    /// Sets up PodmanKubernetesDriver.DownAsync to return success.
    /// </summary>
    public MockDriverPack SetupKubeDown()
    {
      PodmanKubernetesDriver
          .Setup(d => d.DownAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<Unit>.Ok(Unit.Default));
      return this;
    }

    /// <summary>
    /// Sets up PodmanKubernetesDriver.GenerateAsync to return YAML.
    /// </summary>
    public MockDriverPack SetupKubeGenerate(string yaml = "apiVersion: v1\nkind: Pod")
    {
      PodmanKubernetesDriver
          .Setup(d => d.GenerateAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<string>(),
              It.IsAny<CancellationToken>()))
          .ReturnsAsync(CommandResponse<string>.Ok(yaml));
      return this;
    }
  }
}
