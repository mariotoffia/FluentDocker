using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Builders;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Model.Drivers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluentDocker.Tests.Integration
{
  /// <summary>
  /// Executable version of the docs/compose.md access pattern against a real engine,
  /// deliberately WITHOUT <c>WithProjectName</c>: compose derives the project name from the
  /// project directory, so <c>ListServicesAsync</c> and teardown must identify the project
  /// via the compose files rather than a fabricated <c>-p</c> value.
  /// </summary>
  [Trait("Category", "Integration")]
  public class ComposeProjectNameIntegrationTests
  {
    [Fact]
    public async Task UseCompose_WithoutProjectName_ListsServicesAndTearsDown()
    {
      var cancellationToken = TestContext.Current.CancellationToken;
      var dir = Directory.CreateTempSubdirectory("fd-compose-derived-");
      var composeFile = Path.Combine(dir.FullName, "docker-compose.yml");
      await File.WriteAllTextAsync(composeFile,
          "services:\n" +
          "  web:\n" +
          "    image: alpine:latest\n" +
          "    command: [\"tail\", \"-f\", \"/dev/null\"]\n" +
          "    ports:\n" +
          "      - \"80\"\n",
          cancellationToken);

      await using var kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .WithDockerCli("docker", d => d.AsDefault())
          .BuildAsync(cancellationToken: cancellationToken);

      try
      {
        var results = await new Builder()
            .WithinDriver("docker", kernel)
            .UseCompose(c => c
                .WithComposeFile(composeFile)
                .WithWait())
            .BuildAsync(cancellationToken: cancellationToken);

        var compose = results.ComposeServices[0];
        var services = await compose.ListServicesAsync(cancellationToken);
        var web = services.First(s => s.Name == "web");
        Assert.Equal("running", web.State, ignoreCase: true);
        Assert.NotEmpty(web.Publishers);
        Assert.True(web.Publishers.First(p => p.TargetPort == 80).PublishedPort > 0);

        await results.DisposeAsync();

        // Teardown must have removed the derived-name project: listing by file is now empty.
        var driver = kernel.SysCtl<IComposeDriver>("docker");
        var after = await driver.ListAsync(
            new DriverContext("docker"),
            new ComposeListConfig { ComposeFiles = [composeFile] },
            cancellationToken);
        Assert.True(after.Success, after.Error);
        Assert.Empty(after.Data);
      }
      finally
      {
        try
        {
          dir.Delete(recursive: true);
        }
        catch
        {
          // Best-effort scratch cleanup.
        }
      }
    }
  }
}
