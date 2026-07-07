using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Kernel;
using FluentDocker.Kernel;
using FluentDocker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluentDocker.Tests.Documentation
{
  [Trait("Category", "Unit")]
  public class ComposeDocSnippetsTests
  {
    [Fact]
    public async Task ComposeDoc_UsesListServicesAsyncInsteadOfBuildResultsContainers()
    {
      var doc = await File.ReadAllTextAsync(
          FindRepoFile("docs", "compose.md"),
          TestContext.Current.CancellationToken);

      Assert.Contains("ListServicesAsync", doc, StringComparison.Ordinal);
      // The broken pre-3.2 pattern: any `<var>.Containers` on compose build results.
      // BuildResults.Containers (prose note about the type itself) is the one legal mention.
      Assert.DoesNotMatch(@"(?<!Build)[Rr]esults\.Containers", doc);
    }

    [Fact]
    public async Task PublishedPortSnippet_CompilesAgainstCurrentComposeApi()
    {
      var cancellationToken = TestContext.Current.CancellationToken;
      await using var kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
          .BuildAsync(cancellationToken);

      var compose = new Mock<IComposeService>();
      compose.SetupGet(c => c.Name).Returns("docs-compose");
      compose.SetupGet(c => c.ProjectName).Returns("docs-compose");
      compose.SetupGet(c => c.ComposeFiles).Returns(["docker-compose.yml"]);
      compose.SetupGet(c => c.State).Returns(ServiceRunningState.Running);
      compose.SetupGet(c => c.Kernel).Returns(kernel);
      compose.SetupGet(c => c.DriverId).Returns("docker");
      compose.Setup(c => c.ListServicesAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new List<ComposeServiceInfo>
          {
            new ComposeServiceInfo
            {
              Name = "api",
              State = "running",
              Publishers =
              [
                new ComposePublisher
                {
                  TargetPort = 8080,
                  PublishedPort = 49152,
                  Protocol = "tcp"
                }
              ]
            }
          });

      var scope = new BuildScope(kernel, "docker");
      scope.AddResult(compose.Object);
      var results = new BuildResults([scope]);

      var url = await DocumentedApiBaseUrlAsync(results, cancellationToken);

      Assert.Equal("http://localhost:49152", url);
    }

    private static async Task<string> DocumentedApiBaseUrlAsync(
        BuildResults results,
        CancellationToken cancellationToken)
    {
      // Mirrors docs/compose.md "Use in Test Base Classes" exactly — keep in sync.
      var api = (await results.ComposeServices.First().ListServicesAsync(cancellationToken))
        .First(s => s.Name == "api");
      var port = api.Publishers.First(p => p.TargetPort == 8080).PublishedPort;

      return $"http://localhost:{port}";
    }

    private static string FindRepoFile(params string[] parts)
    {
      for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
      {
        var path = Path.Combine(parts.Prepend(dir.FullName).ToArray());
        if (File.Exists(path))
        {
          return path;
        }
      }

      throw new FileNotFoundException($"Could not find {Path.Combine(parts)} from {AppContext.BaseDirectory}.");
    }
  }
}
