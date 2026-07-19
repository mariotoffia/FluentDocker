using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers;
using FluentDocker.Drivers.Docker.Api.Components;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public class DockerApiWaitTimeoutTests
  {
    [Fact]
    public async Task WaitAsync_CompletesAfterConfiguredRequestTimeout()
    {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
      var server = ServeDelayedWaitResponseAsync(listener, cts.Token);

      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ApiVersion = "1.45",
        RequestTimeout = TimeSpan.FromMilliseconds(50),
        ConnectionTimeout = TimeSpan.FromSeconds(2)
      });
      var driver = new DockerApiContainerDriver(connection);
      var context = new DriverContext("docker-api-wait-timeout-test");
      driver.Initialize(context);

      var result = await driver.WaitAsync(context, "ctr1", TestContext.Current.CancellationToken);
      cts.Cancel();
      await server;

      Assert.True(result.Success, result.Error);
      Assert.Equal(0, result.Data.ExitCode);
    }

    private static async Task ServeDelayedWaitResponseAsync(TcpListener listener, CancellationToken ct)
    {
      using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
      await using var stream = client.GetStream();
      using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
      while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct).ConfigureAwait(false)))
      {
      }

      await Task.Delay(150, ct).ConfigureAwait(false);
      var body = Encoding.UTF8.GetBytes(@"{""StatusCode"":0}");
      var header = Encoding.ASCII.GetBytes(
          $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nContent-Type: application/json\r\n\r\n");
      await stream.WriteAsync(header, ct).ConfigureAwait(false);
      await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }
  }
}
