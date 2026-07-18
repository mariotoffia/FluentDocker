using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for <see cref="DockerCliImageDriver"/>: streamed pull/push/build
  /// progress and exit-code propagation, default-tag handling, leading-dash context guards,
  /// culture-invariant date parsing and history JSON failure semantics.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliImageDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task ImagePull_StreamsProgress_AndAllowsLargeOutput()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
if [ "$1" = "pull" ]; then
  i=0
  while [ $i -lt 3000 ]; do
    printf 'layer%04d Downloading ' "$i"
    printf '%01990d\n' 0
    i=$((i+1))
  done
  exit 0
fi
exit 2
""");
      var driver = new DockerCliImageDriver(new FakeResolver(docker));
      driver.Initialize(new DriverContext("docker"));
      var progress = new ListProgress<ImagePullProgress>();

      var result = await driver.PullAsync(
          new DriverContext("docker"),
          "alpine",
          "latest",
          progress,
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.NotEmpty(progress.Items);
      Assert.Contains(progress.Items, p => p.Status.Contains("Downloading", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImageProgressFailures_PropagateRealExitCode()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var docker = CreateFakeDocker("""
#!/bin/sh
case "$1" in
  pull) echo 'pull failed' 1>&2; exit 17 ;;
  push) echo 'push failed' 1>&2; exit 18 ;;
  build) echo 'build failed' 1>&2; exit 19 ;;
esac
exit 2
""");
      var driver = new DockerCliImageDriver(new FakeResolver(docker));
      driver.Initialize(new DriverContext("docker"));

      var pull = await driver.PullAsync(new DriverContext("docker"), "alpine", "latest", cancellationToken: TestContext.Current.CancellationToken);
      var push = await driver.PushAsync(new DriverContext("docker"), "alpine:latest", cancellationToken: TestContext.Current.CancellationToken);
      var build = await driver.BuildAsync(new DriverContext("docker"), new ImageBuildConfig { BuildContext = "." }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.Equal(17, pull.ExitCode);
      Assert.Equal(17, pull.ErrorContext.ExitCode);
      Assert.Equal(18, push.ExitCode);
      Assert.Equal(18, push.ErrorContext.ExitCode);
      Assert.Equal(19, build.ExitCode);
      Assert.Equal(19, build.ErrorContext.ExitCode);
    }

    [Theory]
    [InlineData("redis", "latest", "redis:latest")]
    [InlineData("redis:7", "latest", "redis:7")]
    [InlineData("redis@sha256:abc", "latest", "redis@sha256:abc")]
    [InlineData("registry:5000/redis", "latest", "registry:5000/redis:latest")]
    [InlineData("registry:5000/redis:7", "latest", "registry:5000/redis:7")]
    public async Task ImagePull_AppendsDefaultTagOnlyForBareNames(string image, string tag, string expected)
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"pull-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliImageDriver(new FakeResolver(CreateRecordingDocker(record, "")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.PullAsync(
          new DriverContext("docker"),
          image,
          tag,
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal(["pull", expected], await ReadArgsAsync(record));
    }

    [Fact]
    public async Task ImageBuild_RejectsLeadingDashContext()
    {
      var driver = new DockerCliImageDriver(new FakeResolver("/bin/docker"));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.BuildAsync(new DriverContext("docker"), new ImageBuildConfig
      {
        BuildContext = "--help"
      }, cancellationToken: TestContext.Current.CancellationToken);

      Assert.False(result.Success);
      Assert.Equal(ErrorCodes.General.InvalidArgument, result.ErrorCode);
    }

    [Fact]
    public async Task ImageList_ParsesCliDateUsingInvariantCulture()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var originalCulture = CultureInfo.CurrentCulture;
      try
      {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var driver = new DockerCliImageDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
printf '%s\n' '{"ID":"sha256:abc","Repository":"repo","Tag":"latest","Size":"1B","CreatedAt":"12/31/2024 1:02:03 PM"}'
""")));
        driver.Initialize(new DriverContext("docker"));

        var result = await driver.ListAsync(new DriverContext("docker"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2024, Assert.Single(result.Data).Created.Year);
      }
      finally
      {
        CultureInfo.CurrentCulture = originalCulture;
      }
    }

    [Fact]
    public async Task ImageHistory_AllJsonLinesFail_ReturnsFailure_EmptyOutputReturnsEmpty()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var invalid = new DockerCliImageDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
echo not-json
exit 0
""")));
      invalid.Initialize(new DriverContext("docker"));

      var invalidResult = await invalid.HistoryAsync(new DriverContext("docker"), "alpine", TestContext.Current.CancellationToken);
      Assert.False(invalidResult.Success);

      var empty = new DockerCliImageDriver(new FakeResolver(CreateFakeDocker("""
#!/bin/sh
exit 0
""")));
      empty.Initialize(new DriverContext("docker"));

      var emptyResult = await empty.HistoryAsync(new DriverContext("docker"), "alpine", TestContext.Current.CancellationToken);
      Assert.True(emptyResult.Success, emptyResult.Error);
      Assert.Empty(emptyResult.Data);
    }

    private sealed class ListProgress<T> : IProgress<T>
    {
      public List<T> Items { get; } = [];

      public void Report(T value) => Items.Add(value);
    }
  }
}
