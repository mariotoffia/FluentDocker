using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Integration tests for IComposeDriver.PushAsync.
  /// Requires a local Docker registry — marked ManualOnly so CI skips them.
  /// </summary>
  public partial class ComposeDriverTests
  {
    #region PushAsync Tests

    /// <summary>
    /// Builds a trivial image via compose and pushes it to a local registry.
    /// Requires Docker to be running. Uses a temporary registry:2 container.
    /// </summary>
    [Trait("Category", "ManualOnly")]
    [Fact]
    public async Task Push_ToLocalRegistry_Succeeds()
    {
      const string registryPort = "5051";
      string registryId = null;
      string tempDir = null;
      var projectName = UniqueName("push");

      try
      {
        // 1. Start a local registry container
        registryId = await RunContainerAsync("registry:2",
            new ContainerCreateConfig
            {
              PortBindings = new Dictionary<string, string>
              {
                ["5000/tcp"] = registryPort
              }
            });
        await Task.Delay(3000); // Wait for registry to be ready

        // 2. Create temp compose project with a buildable service
        tempDir = Path.Combine(Path.GetTempPath(), $"push-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var dockerfile = Path.Combine(tempDir, "Dockerfile");
        File.WriteAllText(dockerfile,
            "FROM alpine:latest\nRUN echo hello > /hello.txt\n");

        var composeFile = Path.Combine(tempDir, "docker-compose.yml");
        File.WriteAllText(composeFile,
            $"services:\n" +
            $"  pushtest:\n" +
            $"    image: localhost:{registryPort}/push-test:latest\n" +
            $"    build:\n" +
            $"      context: .\n" +
            $"      dockerfile: Dockerfile\n");

        // 3. Build the image via compose
        var buildResult = await ComposeDriver.BuildAsync(Context,
            new ComposeBuildConfig
            {
              ComposeFiles = new List<string> { composeFile },
              ProjectName = projectName,
              NoCache = true
            });
        Assert.True(buildResult.Success, $"Build failed: {buildResult.Error}");

        // 4. Push to the local registry
        var pushResult = await ComposeDriver.PushAsync(Context,
            new ComposeFileConfig
            {
              ComposeFiles = new List<string> { composeFile },
              ProjectName = projectName
            });
        Assert.True(pushResult.Success, $"Push failed: {pushResult.Error}");
      }
      finally
      {
        // 5. Clean up compose project
        if (tempDir != null && File.Exists(Path.Combine(tempDir, "docker-compose.yml")))
        {
          await ComposeDriver.DownAsync(Context, new ComposeDownConfig
          {
            ComposeFiles = new List<string>
                { Path.Combine(tempDir, "docker-compose.yml") },
            ProjectName = projectName,
            RemoveImages = "local"
          });
        }

        // Clean up registry container
        await RemoveContainerAsync(registryId);

        // Clean up temp directory
        if (tempDir != null && Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    /// <summary>
    /// Pushes a specific service (not all services) to a local registry.
    /// </summary>
    [Trait("Category", "ManualOnly")]
    [Fact]
    public async Task Push_SpecificService_ToLocalRegistry_Succeeds()
    {
      const string registryPort = "5052";
      string registryId = null;
      string tempDir = null;
      var projectName = UniqueName("push");

      try
      {
        registryId = await RunContainerAsync("registry:2",
            new ContainerCreateConfig
            {
              PortBindings = new Dictionary<string, string>
              {
                ["5000/tcp"] = registryPort
              }
            });
        await Task.Delay(3000);

        tempDir = Path.Combine(Path.GetTempPath(), $"push-svc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        File.WriteAllText(Path.Combine(tempDir, "Dockerfile"),
            "FROM alpine:latest\nRUN echo svc > /svc.txt\n");

        var composeFile = Path.Combine(tempDir, "docker-compose.yml");
        File.WriteAllText(composeFile,
            $"services:\n" +
            $"  svc-a:\n" +
            $"    image: localhost:{registryPort}/push-svc-a:latest\n" +
            $"    build:\n" +
            $"      context: .\n" +
            $"      dockerfile: Dockerfile\n" +
            $"  svc-b:\n" +
            $"    image: localhost:{registryPort}/push-svc-b:latest\n" +
            $"    build:\n" +
            $"      context: .\n" +
            $"      dockerfile: Dockerfile\n");

        // Build all services
        var buildResult = await ComposeDriver.BuildAsync(Context,
            new ComposeBuildConfig
            {
              ComposeFiles = new List<string> { composeFile },
              ProjectName = projectName,
              NoCache = true
            });
        Assert.True(buildResult.Success, $"Build failed: {buildResult.Error}");

        // Push only svc-a
        var pushResult = await ComposeDriver.PushAsync(Context,
            new ComposeFileConfig
            {
              ComposeFiles = new List<string> { composeFile },
              ProjectName = projectName,
              Services = new List<string> { "svc-a" }
            });
        Assert.True(pushResult.Success,
            $"Push svc-a failed: {pushResult.Error}");
      }
      finally
      {
        if (tempDir != null && File.Exists(Path.Combine(tempDir, "docker-compose.yml")))
        {
          await ComposeDriver.DownAsync(Context, new ComposeDownConfig
          {
            ComposeFiles = new List<string>
                { Path.Combine(tempDir, "docker-compose.yml") },
            ProjectName = projectName,
            RemoveImages = "local"
          });
        }

        await RemoveContainerAsync(registryId);

        if (tempDir != null && Directory.Exists(tempDir))
          Directory.Delete(tempDir, true);
      }
    }

    #endregion
  }
}
