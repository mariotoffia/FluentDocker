using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Cli.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.Docker
{
  /// <summary>
  /// Production-readiness coverage for <see cref="DockerCliStackDriver"/>: preserving stack names
  /// under <c>--no-trunc</c>, and translating list/task/service filters to CLI flags while
  /// suppressing the removed Kubernetes-orchestrator flags modern Docker would reject.
  /// </summary>
  [Trait("Category", "Unit")]
  [Trait("Requires", "PosixShell")]
  public sealed class DockerCliStackDriverProductionReadinessTests : DockerCliFakeDockerTestBase
  {
    [Fact]
    public async Task StackPs_NoTrunc_DoesNotRewriteStackName()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"stack-args-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliStackDriver(new FakeResolver(CreateRecordingDocker(record, "")));
      driver.Initialize(new DriverContext("docker"));

      var result = await driver.GetTasksAsync(
          new DriverContext("docker"),
          "my stack ps",
          new StackTaskFilter { NoTrunc = true },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success, result.Error);
      Assert.Equal("my stack ps", (await ReadArgsAsync(record)).Last());
    }

    [Fact]
    public async Task StackFilters_ArePassedToCli()
    {
      if (OperatingSystem.IsWindows())
        Assert.Skip("POSIX shell script fake docker; not applicable on Windows");

      var record = Path.Combine(TestOutputDirectory(), $"stack-{Guid.NewGuid():N}.txt");
      var driver = new DockerCliStackDriver(new FakeResolver(CreateFakeDocker($$"""
#!/bin/sh
printf '%s\n' "$@" > "{{record}}"
case "$*" in
  *"stack ls"*) printf '%s\n' '{"Name":"demo","Services":1,"Orchestrator":"swarm","Namespace":"ns"}' ;;
  *"stack ps"*) printf '%s\n' '{"ID":"task1","Name":"demo_web.1","Image":"nginx","Node":"node1","DesiredState":"Running","CurrentState":"Running"}' ;;
  *"stack services"*) printf '%s\n' '{"ID":"svc1","Name":"demo_web","Mode":"replicated","Replicas":"1/1","Image":"nginx"}' ;;
esac
""")));
      driver.Initialize(new DriverContext("docker"));
      var context = new DriverContext("docker");

      var stacks = await driver.ListAsync(context, new StackListFilter
      {
        Orchestrator = "kubernetes",
        Namespace = "ns",
        AllNamespaces = true,
        KubeConfig = ".out/kubeconfig"
      }, TestContext.Current.CancellationToken);
      var listArgs = await ReadArgsAsync(record);

      var tasks = await driver.GetTasksAsync(context, "demo", new StackTaskFilter
      {
        Id = "task1",
        Name = "web",
        Node = "node1",
        DesiredState = "running",
        NoTrunc = true,
        NoResolve = true,
        Quiet = true,
        Orchestrator = "swarm",
        Namespace = "ns",
        KubeConfig = ".out/kubeconfig"
      }, TestContext.Current.CancellationToken);
      var taskArgs = await ReadArgsAsync(record);

      var services = await driver.GetServicesAsync(context, "demo", new StackServiceFilter
      {
        Id = "svc1",
        Name = "web",
        Labels = { { "tier", "frontend" } },
        Quiet = true,
        Orchestrator = "swarm",
        Namespace = "ns",
        KubeConfig = ".out/kubeconfig"
      }, TestContext.Current.CancellationToken);
      var serviceArgs = await ReadArgsAsync(record);

      Assert.True(stacks.Success, stacks.Error);
      // Kubernetes-orchestrator flags were removed from modern Docker; setting the (retained)
      // filter fields must not inject flags the CLI would reject with "unknown flag" (exit 125).
      Assert.DoesNotContain("--orchestrator", listArgs);
      Assert.DoesNotContain("--namespace", listArgs);
      Assert.DoesNotContain("--all-namespaces", listArgs);
      Assert.DoesNotContain("--kubeconfig", listArgs);

      Assert.True(tasks.Success, tasks.Error);
      Assert.Equal("task1", Assert.Single(tasks.Data).Id);
      Assert.Null(tasks.Data[0].Name);
      Assert.Contains("id=task1", taskArgs);
      Assert.Contains("name=web", taskArgs);
      Assert.Contains("node=node1", taskArgs);
      Assert.Contains("desired-state=running", taskArgs);
      Assert.Contains("--no-trunc", taskArgs);
      Assert.Contains("--no-resolve", taskArgs);
      Assert.DoesNotContain("--orchestrator", taskArgs);
      Assert.DoesNotContain("--namespace", taskArgs);
      Assert.DoesNotContain("--kubeconfig", taskArgs);

      Assert.True(services.Success, services.Error);
      Assert.Equal("svc1", Assert.Single(services.Data).Id);
      Assert.Null(services.Data[0].Name);
      Assert.Contains("id=svc1", serviceArgs);
      Assert.Contains("name=web", serviceArgs);
      Assert.Contains("label=tier=frontend", serviceArgs);
      Assert.DoesNotContain("--orchestrator", serviceArgs);
      Assert.DoesNotContain("--namespace", serviceArgs);
      Assert.DoesNotContain("--kubeconfig", serviceArgs);
    }
  }
}
