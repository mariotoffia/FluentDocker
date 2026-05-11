using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Model.Containers;
using Xunit;

namespace FluentDocker.Tests.Integration.DockerCliDriver
{
  /// <summary>
  /// Advanced wait condition tests: custom lambda, timeouts, and helper methods.
  /// </summary>
  [Trait("Category", "Integration")]
  [Trait("Category", "WaitCondition")]
  public partial class WaitConditionTests
  {
    #region Custom Lambda Wait Tests

    [Fact]
    public async Task WaitWithLambda_CustomCondition_WaitsUntilTrue()
    {
      string? containerId = null;
      try
      {
        // Arrange - Start postgres
        var runResult = await ContainerDriver.RunAsync(Context, new ContainerCreateConfig
        {
          Image = PostgresImage,
          Environment = new Dictionary<string, string>
          {
            ["POSTGRES_PASSWORD"] = "mysecretpassword"
          },
          PortBindings = new Dictionary<string, string>
          {
            ["5432/tcp"] = "0"
          },
          Detach = true
        }, TestContext.Current.CancellationToken);

        Assert.True(runResult.Success);
        containerId = runResult.Data.Id;

        // Get port
        var inspect = await ContainerDriver.InspectAsync(Context, containerId, TestContext.Current.CancellationToken);
        var portBinding = inspect.Data.NetworkSettings?.Ports?["5432/tcp"];
        Assert.NotNull(portBinding);
        var hostPort = int.Parse(portBinding[0].HostPort);

        // Act - Wait with custom lambda
        var invocationCount = 0;
        var success = await WaitWithConditionAsync(async () =>
        {
          invocationCount++;

          // Try to connect to postgres port
          try
          {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", hostPort);
            return true;
          }
          catch
          {
            return false;
          }
        }, TimeSpan.FromSeconds(30));

        // Assert
        Assert.True(success, "Custom condition should have succeeded");
        Assert.True(invocationCount >= 1, "Lambda should have been invoked at least once");
      }
      finally
      {
        if (containerId != null)
          await RemoveContainerAsync(containerId!);
      }
    }

    #endregion

    #region Timeout Tests

    [Fact]
    public async Task WaitForPort_WhenTimeout_ReturnsFalse()
    {
      // Act - Wait for a port that will never open
      var isOpen = await WaitForPortAsync("127.0.0.1", 59999, TimeSpan.FromSeconds(2));

      // Assert
      Assert.False(isOpen, "Should timeout waiting for non-existent port");
    }

    [Fact]
    public async Task WaitForHttp_WhenTimeout_ReturnsFalse()
    {
      // Act - Wait for HTTP on a port that will never respond
      var isReady = await WaitForHttpAsync("http://127.0.0.1:59999/", TimeSpan.FromSeconds(2));

      // Assert
      Assert.False(isReady, "Should timeout waiting for non-existent HTTP endpoint");
    }

    #endregion

    #region Helper Methods

    private static async Task<bool> WaitForPortAsync(string host, int port, TimeSpan timeout)
    {
      var endTime = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < endTime)
      {
        try
        {
          using var client = new TcpClient();
          var connectTask = client.ConnectAsync(host, port);
          if (await Task.WhenAny(connectTask, Task.Delay(1000)) == connectTask)
          {
            if (client.Connected)
              return true;
          }
        }
        catch
        {
          // Connection failed, retry
        }

        await Task.Delay(500);
      }

      return false;
    }

    private async Task<bool> WaitForProcessAsync(string containerId, string processName, TimeSpan timeout)
    {
      var endTime = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < endTime)
      {
        var topResult = await ContainerDriver.TopAsync(Context, containerId);
        if (topResult.Success && topResult.Data?.Processes != null)
        {
          foreach (var process in topResult.Data.Processes)
          {
            // Check if any column contains the process name
            if (process.Any(v => v.Contains(processName, StringComparison.OrdinalIgnoreCase)))
            {
              return true;
            }
          }
        }

        await Task.Delay(500);
      }

      return false;
    }

    private static async Task<bool> WaitForHttpAsync(string url, TimeSpan timeout)
    {
      var endTime = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < endTime)
      {
        try
        {
          var response = await HttpClient.GetAsync(url);
          if (response.IsSuccessStatusCode)
            return true;
        }
        catch
        {
          // Request failed, retry
        }

        await Task.Delay(500);
      }

      return false;
    }

    private static async Task<bool> WaitForHttpWithContentAsync(string url, string expectedContent, TimeSpan timeout)
    {
      var endTime = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < endTime)
      {
        try
        {
          var response = await HttpClient.GetAsync(url);
          if (response.IsSuccessStatusCode)
          {
            var content = await response.Content.ReadAsStringAsync();
            if (content.Contains(expectedContent, StringComparison.OrdinalIgnoreCase))
              return true;
          }
        }
        catch
        {
          // Request failed, retry
        }

        await Task.Delay(500);
      }

      return false;
    }

    private async Task<bool> WaitForHealthyAsync(string containerId, TimeSpan timeout)
    {
      var endTime = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < endTime)
      {
        var inspect = await ContainerDriver.InspectAsync(Context, containerId);
        if (inspect.Success && inspect.Data.State?.Health != null)
        {
          if (inspect.Data.State.Health.Status == HealthState.Healthy)
            return true;
        }

        await Task.Delay(1000);
      }

      return false;
    }

    private static async Task<bool> WaitWithConditionAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
      var endTime = DateTime.UtcNow + timeout;

      while (DateTime.UtcNow < endTime)
      {
        try
        {
          if (await condition())
            return true;
        }
        catch
        {
          // Condition threw, retry
        }

        await Task.Delay(500);
      }

      return false;
    }

    #endregion
  }
}
