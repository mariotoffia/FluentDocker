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
  public sealed class DockerApiDurationParsingTests
  {
    [Fact]
    public async Task CreateAsync_HealthcheckDurations_SupportFractionalAndSubMillisecondUnits()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""abc123"",""Warnings"":[]}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(new DriverContext("docker-api-duration-test"));

      await driver.CreateAsync(
          new DriverContext("docker-api-duration-test"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            HealthCheck = new HealthCheckConfig
            {
              Test = ["CMD", "true"],
              Interval = "1.5s",
              Timeout = "500ms",
              StartPeriod = "2m30s"
            }
          },
          TestContext.Current.CancellationToken);

      var request = mock.GetRequests().Single(r => r.Method == "POST");
      using var doc = JsonDocument.Parse(request.Body!);
      var healthcheck = GetProperty(doc.RootElement, "Healthcheck");
      Assert.Equal(1_500_000_000, GetProperty(healthcheck, "Interval").GetInt64());
      Assert.Equal(500_000_000, GetProperty(healthcheck, "Timeout").GetInt64());
      Assert.Equal(150_000_000_000, GetProperty(healthcheck, "StartPeriod").GetInt64());
    }

    [Fact]
    public async Task CreateAsync_HealthcheckDuration_SupportsMicroseconds()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""abc123"",""Warnings"":[]}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(new DriverContext("docker-api-duration-test"));

      await driver.CreateAsync(
          new DriverContext("docker-api-duration-test"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            HealthCheck = new HealthCheckConfig
            {
              Test = ["CMD", "true"],
              Timeout = "100us"
            }
          },
          TestContext.Current.CancellationToken);

      var request = mock.GetRequests().Single(r => r.Method == "POST");
      using var doc = JsonDocument.Parse(request.Body!);
      var healthcheck = GetProperty(doc.RootElement, "Healthcheck");
      Assert.Equal(100_000, GetProperty(healthcheck, "Timeout").GetInt64());
    }

    [Fact]
    public async Task CreateAsync_HealthcheckDuration_OverflowOmitsDuration()
    {
      var mock = new MockDockerApiConnection();
      mock.SetupPost("/containers/create", 201, @"{""Id"":""abc123"",""Warnings"":[]}");
      var driver = new DockerApiContainerDriver(mock);
      driver.Initialize(new DriverContext("docker-api-duration-test"));

      await driver.CreateAsync(
          new DriverContext("docker-api-duration-test"),
          new ContainerCreateConfig
          {
            Image = "alpine",
            HealthCheck = new HealthCheckConfig
            {
              Test = ["CMD", "true"],
              Timeout = "100000000000h"
            }
          },
          TestContext.Current.CancellationToken);

      var request = mock.GetRequests().Single(r => r.Method == "POST");
      using var doc = JsonDocument.Parse(request.Body!);
      var healthcheck = GetProperty(doc.RootElement, "Healthcheck");
      Assert.False(TryGetProperty(healthcheck, "Timeout", out _));
    }

    private static JsonElement GetProperty(JsonElement element, string name)
    {
      foreach (var property in element.EnumerateObject())
      {
        if (string.Equals(property.Name, name, System.StringComparison.OrdinalIgnoreCase))
          return property.Value;
      }
      throw new System.Collections.Generic.KeyNotFoundException(name);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
      foreach (var property in element.EnumerateObject())
      {
        if (string.Equals(property.Name, name, System.StringComparison.OrdinalIgnoreCase))
        {
          value = property.Value;
          return true;
        }
      }
      value = default;
      return false;
    }
  }
}
