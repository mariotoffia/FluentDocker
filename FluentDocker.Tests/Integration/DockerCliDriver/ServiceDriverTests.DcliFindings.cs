using System.Threading.Tasks;
using FluentDocker.Drivers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  public partial class ServiceDriverTests
  {
    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Requires", "Docker")]
    [Trait("Requires", "Swarm")]
    public async Task GetLogs_MergesStdoutAndStderr()
    {
      var serviceName = UniqueName("svc");
      try
      {
        await EnsureImageAsync(TestImage);
        var createResult = await ServiceDriver.CreateAsync(Context,
            new ServiceCreateConfig
            {
              Name = serviceName,
              Image = TestImage,
              Replicas = 1,
              Detach = true,
              Command = ["sh", "-c", "printf 'stdout-line\n'; printf 'stderr-line\n' 1>&2; sleep 30"]
            }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(createResult.Success, $"Create failed: {createResult.Error}");
        await WaitForServiceReplicasAsync(serviceName, 1, 30);

        var logsResult = await ServiceDriver.GetLogsAsync(Context,
            serviceName,
            new ServiceLogsConfig { Tail = 20 },
            TestContext.Current.CancellationToken);

        Assert.True(logsResult.Success, $"GetLogs failed: {logsResult.Error}");
        Assert.Contains("stdout-line", logsResult.Data);
        Assert.Contains("stderr-line", logsResult.Data);
      }
      finally
      {
        await RemoveServiceSafeAsync(serviceName);
      }
    }
  }
}
