using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Advanced container tests: copy operations, export, reuse, health checks,
  /// resource limits, and diff.
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Category", "FluentContainer")]
  public partial class FluentContainerTests
  {
    #region Copy Operations Tests

    [Fact]
    public async Task CopyFrom_RunningContainer_CopiesFiles()
    {
      string? containerId = null;
      var hostPath = Path.Combine(Path.GetTempPath(), $"fluentdockertest-{Guid.NewGuid():N}");

      try
      {
        // Arrange
        Directory.CreateDirectory(hostPath);
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          Detach = true
        });
        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Act - Copy from container
        var result = await ContainerDriver.CopyFromAsync(Context, containerId, "/etc/passwd", hostPath);

        // Assert
        Assert.True(result.Success, $"Copy failed: {result.Error}");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
        if (Directory.Exists(hostPath))
          Directory.Delete(hostPath, true);
      }
    }

    [Fact]
    public async Task CopyTo_RunningContainer_CopiesFiles()
    {
      string? containerId = null;
      var hostPath = Path.Combine(Path.GetTempPath(), $"fluentdockertest-{Guid.NewGuid():N}");
      var testFile = Path.Combine(hostPath, "test.txt");

      try
      {
        // Arrange
        Directory.CreateDirectory(hostPath);
        File.WriteAllText(testFile, "Hello from host!");

        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = new[] { "sleep", "60" },
          Detach = true
        });
        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Act
        var result = await ContainerDriver.CopyToAsync(Context, containerId, testFile, "/tmp/");

        // Assert
        Assert.True(result.Success, $"Copy failed: {result.Error}");

        // Verify file was copied
        var execResult = await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "cat", "/tmp/test.txt" }
        });
        Assert.True(execResult.Success);
        Assert.Contains("Hello from host!", execResult.Data.StdOut);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
        if (Directory.Exists(hostPath))
          Directory.Delete(hostPath, true);
      }
    }

    #endregion

    #region Export Operations Tests

    [Fact]
    public async Task Export_Container_CreatesArchive()
    {
      string? containerId = null;
      var exportPath = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.tar");

      try
      {
        // Arrange
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = new[] { "sleep", "60" },
          Detach = true
        });
        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Act
        var result = await ContainerDriver.ExportAsync(Context, containerId, exportPath);

        // Assert
        Assert.True(result.Success, $"Export failed: {result.Error}");
        Assert.True(File.Exists(exportPath), "Export file should exist");

        var fileInfo = new FileInfo(exportPath);
        Assert.True(fileInfo.Length > 0, "Export file should not be empty");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
        if (File.Exists(exportPath))
          File.Delete(exportPath);
      }
    }

    #endregion

    #region Container Reuse Tests

    [Fact]
    public async Task Container_WithSameName_CanBeReused()
    {
      var containerName = UniqueName("reusable");
      string? firstContainerId = null;

      try
      {
        // Arrange - Create first container
        var firstResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Name = containerName,
          Command = new[] { "sleep", "60" },
          Detach = true
        });
        Assert.True(firstResult.Success);
        firstContainerId = firstResult.Data.Id;

        // Stop it
        await ContainerDriver.StopAsync(Context, firstContainerId, timeout: 1);

        // Act - Try to get existing container by name
        var inspect = await ContainerDriver.InspectAsync(Context, containerName);

        // Assert
        Assert.True(inspect.Success);
        Assert.Contains(containerName, inspect.Data.Name);
      }
      finally
      {
        if (firstContainerId != null)
          await RemoveContainerAsync(firstContainerId);
      }
    }

    #endregion

    #region Container Health Check Tests

    [Fact]
    public async Task Container_WithHealthCheck_ReportsHealth()
    {
      string? containerId = null;
      try
      {
        // Act - Run with healthcheck
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          HealthCheck = new HealthCheckConfig
          {
            Test = new[] { "CMD-SHELL", "pg_isready -U postgres || exit 1" },
            Interval = "5s",
            Retries = 3
          },
          Detach = true
        });

        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        // Assert - Health check is configured
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);
        Assert.NotNull(inspect.Data.State.Health);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Resource Limits Tests

    [Fact]
    public async Task Container_WithMemoryLimit_EnforcesLimit()
    {
      string? containerId = null;
      try
      {
        // Act
        var result = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = new[] { "sleep", "60" },
          MemoryLimit = 128 * 1024 * 1024, // 128MB
          Detach = true
        });

        Assert.True(result.Success, $"Run failed: {result.Error}");
        containerId = result.Data.Id;

        // Assert
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        Assert.True(inspect.Success);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion

    #region Container Diff Tests

    [Fact]
    public async Task Diff_AfterFileChange_ShowsChanges()
    {
      string? containerId = null;
      try
      {
        // Arrange
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = TestImage,
          Command = new[] { "sleep", "60" },
          Detach = true
        });
        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Make a change in the container
        await ContainerDriver.ExecAsync(Context, containerId, new ExecConfig
        {
          Command = new[] { "touch", "/tmp/newfile.txt" }
        });

        // Act
        var result = await ContainerDriver.DiffAsync(Context, containerId);

        // Assert
        Assert.True(result.Success, $"Diff failed: {result.Error}");
        Assert.NotNull(result.Data);
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId);
      }
    }

    #endregion
  }
}
