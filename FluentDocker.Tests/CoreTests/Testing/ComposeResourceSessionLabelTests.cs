using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;
using FluentDocker.Testing.Core;
using FluentDocker.Tests.Mocks;
using Moq;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Testing
{
  [Trait("Category", "Unit")]
  public class ComposeResourceSessionLabelTests : MockKernelTestBase, IAsyncLifetime
  {
    public async ValueTask InitializeAsync()
    {
      await InitializeMockKernelAsync();
    }

    [Fact]
    public async Task InitializeAsync_WhenSessionLabelsEnabled_AddsOverlayComposeFileWithLabels()
    {
      ComposeConfigConfig configConfig = null;
      ComposeUpConfig config = null;
      MockPack.SetupComposeStop()
          .SetupComposeDown();
      MockPack.ComposeDriver
          .Setup(d => d.ConfigAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeConfigConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ComposeConfigConfig, CancellationToken>((_, cfg, _) => configConfig = cfg)
          .ReturnsAsync(CommandResponse<string>.Ok("""
              {
                "services": { "web": {} },
                "networks": { "default": {} },
                "volumes": { "data": {} }
              }
              """));
      MockPack.ComposeDriver
          .Setup(d => d.UpAsync(
              It.IsAny<DriverContext>(),
              It.IsAny<ComposeUpConfig>(),
              It.IsAny<CancellationToken>()))
          .Callback<DriverContext, ComposeUpConfig, CancellationToken>((_, cfg, _) => config = cfg)
          .ReturnsAsync(CommandResponse<ComposeUpResult>.Ok(new ComposeUpResult { ProjectName = "labels" }));

      var resource = new ComposeResource(
          Kernel,
          builder => builder.WithComposeFile("compose.yml").WithProjectName("labels").WithProfiles("debug"),
          new DockerResourceOptions
          {
            CleanupOrphansOnInit = false,
            SessionId = "session-123"
          });

      await resource.InitializeAsync(TestContext.Current.CancellationToken);

      var overlayFiles = config.ComposeFiles
          .Where(path => path.Contains("compose-labels-", StringComparison.Ordinal))
          .ToList();
      var overlay = Assert.Single(overlayFiles);
      Assert.Equal("debug", configConfig.Environment["COMPOSE_PROFILES"]);
      var json = await File.ReadAllTextAsync(overlay, TestContext.Current.CancellationToken);
      Assert.Contains("\"fluentdocker.session\": \"session-123\"", json);
      Assert.Contains("\"fluentdocker.managed\": \"true\"", json);
      Assert.Contains("\"services\"", json);
      Assert.Contains("\"networks\"", json);
      Assert.Contains("\"volumes\"", json);

      await resource.DisposeAsync();

      Assert.False(File.Exists(overlay));
    }
  }
}
