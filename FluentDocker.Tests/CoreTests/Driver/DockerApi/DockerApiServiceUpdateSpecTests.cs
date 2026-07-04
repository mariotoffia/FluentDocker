using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiServiceUpdateSpecTests
  {
    // ponytail: Full-spec update coverage should add a single-node swarm smoke tier when CI can run one.
    private static readonly DriverContext Ctx = new("docker-api-service-update-test");

    [Fact]
    public async Task ScaleAsync_PostsFullCurrentSpecWithMutatedReplicas()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/services/svc-abc", 200,
          @"{""ID"":""svc-abc"",""Version"":{""Index"":42},""Spec"":{""Name"":""web"",""TaskTemplate"":{""ContainerSpec"":{""Image"":""nginx:latest""}},""Mode"":{""Replicated"":{""Replicas"":3}}}}");
      mock.SetupPost("/services/svc-abc/update", 200, "{}");
      var driver = new DockerApiServiceDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.ScaleAsync(
          Ctx, new() { ["svc-abc"] = 5 },
          cancellationToken: TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var request = mock.GetRequests().Single(r => r.Method == "POST");
      using var doc = JsonDocument.Parse(request.Body!);
      var spec = doc.RootElement;
      Assert.Equal("web", spec.GetProperty("Name").GetString());
      Assert.Equal("nginx:latest", spec.GetProperty("TaskTemplate")
          .GetProperty("ContainerSpec").GetProperty("Image").GetString());
      Assert.Equal(5, spec.GetProperty("Mode")
          .GetProperty("Replicated").GetProperty("Replicas").GetInt32());
    }

    [Fact]
    public async Task UpdateAsync_EnvAdd_PreservesBareExistingEnvEntry()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupGet("/services/svc-env", 200,
          @"{""ID"":""svc-env"",""Version"":{""Index"":7},""Spec"":{""Name"":""web"",""TaskTemplate"":{""ContainerSpec"":{""Image"":""nginx:latest"",""Env"":[""PATH=/usr/bin"",""SECRET_TOKEN"",""TZ=UTC""]}},""Mode"":{""Replicated"":{""Replicas"":1}}}}");
      mock.SetupPost("/services/svc-env/update", 200, "{}");
      var driver = new DockerApiServiceDriver(mock);
      driver.Initialize(Ctx);

      var result = await driver.UpdateAsync(
          Ctx, "svc-env", new() { EnvAdd = { ["NEW_VAR"] = "1" } },
          TestContext.Current.CancellationToken);

      Assert.True(result.Success);
      var request = mock.GetRequests().Single(r => r.Method == "POST");
      using var doc = JsonDocument.Parse(request.Body!);
      var env = doc.RootElement.GetProperty("TaskTemplate")
          .GetProperty("ContainerSpec").GetProperty("Env")
          .EnumerateArray().Select(static value => value.GetString()).ToArray();
      Assert.Contains("SECRET_TOKEN", env);
      Assert.Contains("NEW_VAR=1", env);
    }
  }
}
