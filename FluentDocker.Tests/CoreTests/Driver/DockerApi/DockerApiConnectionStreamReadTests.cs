using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentDocker.Drivers.Docker.Api.Connection;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Driver.DockerApi
{
  [Trait("Category", "Unit")]
  public sealed class DockerApiConnectionStreamReadTests
  {
    /// <summary>
    /// Smoke test for the byte[] ReadAsync overload: with a real HTTP content stream both
    /// the override and the base-class BeginRead fallback return the body, so this pins
    /// reachability, not delegation (ResponseOwningStream is internal; the override
    /// delegating to the Memory-based path is verified by review, not assertable here).
    /// </summary>
    [Fact]
    public async Task GetStreamAsync_ByteArrayReadAsync_ReadsResponseBody()
    {
      using var listener = new TcpListener(IPAddress.Loopback, 0);
      listener.Start();
      var endpoint = (IPEndPoint)listener.LocalEndpoint;
      var server = ServeBodyAsync(listener, "hello", TestContext.Current.CancellationToken);

      await using var connection = new DockerApiConnection(new DockerApiConnectionConfig
      {
        Host = $"tcp://127.0.0.1:{endpoint.Port}",
        ApiVersion = "1.45",
        RequestTimeout = TimeSpan.FromSeconds(2),
        ConnectionTimeout = TimeSpan.FromSeconds(2)
      });

      await using var stream = await connection.GetStreamAsync(
          "/containers/ctr/logs", TestContext.Current.CancellationToken);
      var buffer = new byte[5];
#pragma warning disable CA1835 // This test exercises the byte[] overload intentionally.
      var read = await stream.ReadAsync(
          buffer, 0, buffer.Length, TestContext.Current.CancellationToken);
#pragma warning restore CA1835
      await server;

      Assert.Equal(5, read);
      Assert.Equal("hello", Encoding.UTF8.GetString(buffer));
    }

    private static async Task ServeBodyAsync(
        TcpListener listener, string bodyText, CancellationToken ct)
    {
      using var client = await listener.AcceptTcpClientAsync(ct);
      await using var stream = client.GetStream();
      using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
      while (!string.IsNullOrEmpty(await reader.ReadLineAsync(ct)))
      {
      }

      var body = Encoding.UTF8.GetBytes(bodyText);
      var header = Encoding.ASCII.GetBytes(
          $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\n\r\n");
      await stream.WriteAsync(header, ct);
      await stream.WriteAsync(body, ct);
    }
  }
}
