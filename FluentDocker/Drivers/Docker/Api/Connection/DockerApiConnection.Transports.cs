using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  /// <summary>
  /// Transport-handler factories for the Docker API connection: scheme dispatch plus the Unix
  /// domain socket and Windows named pipe handlers. The TCP/TLS handler lives in its own partial.
  /// </summary>
  public sealed partial class DockerApiConnection
  {
    // ownedCertificates collects every X509Certificate2 created here so the connection
    // instance can dispose them; the unix-socket and named-pipe paths add none.
    private static (SocketsHttpHandler handler, string baseAddress) CreateHandler(
        string host, DockerApiConnectionConfig config, List<X509Certificate2> ownedCertificates)
    {
      var uri = new Uri(host);

      return uri.Scheme.ToLowerInvariant() switch
      {
        "unix" => CreateUnixSocketHandler(uri, config),
        "npipe" => CreateNamedPipeHandler(uri, config),
        "tcp" => CreateTcpHandler(uri, config, useTls: config.UseTls, ownedCertificates),
        "http" => CreateTcpHandler(uri, config, useTls: false, ownedCertificates),
        "https" => CreateTcpHandler(uri, config, useTls: true, ownedCertificates),
        _ => throw new ArgumentException($"Unsupported URI scheme: {uri.Scheme}. " +
            "Use unix://, npipe://, tcp://, http://, or https://", nameof(host))
      };
    }

    private static (SocketsHttpHandler, string) CreateUnixSocketHandler(
        Uri uri, DockerApiConnectionConfig config)
    {
      var socketPath = Uri.UnescapeDataString(uri.AbsolutePath);
      var handler = new SocketsHttpHandler
      {
        ConnectCallback = async (context, ct) =>
        {
          var socket = new Socket(
                      AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
          try
          {
            var endpoint = new UnixDomainSocketEndPoint(socketPath);
            await socket.ConnectAsync(endpoint, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
          }
          catch
          {
            // NetworkStream never took ownership — dispose the socket so a failed
            // connect (bad path, timeout, cancellation) does not leak the descriptor.
            socket.Dispose();
            throw;
          }
        },
        ConnectTimeout = config.ConnectionTimeout
      };

      return (handler, "http://localhost");
    }

    private static (SocketsHttpHandler, string) CreateNamedPipeHandler(
        Uri uri, DockerApiConnectionConfig config)
    {
      if (!string.IsNullOrEmpty(uri.Host) &&
          !string.Equals(uri.Host, ".", StringComparison.Ordinal) &&
          !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException(
            "Remote Docker named pipes are not supported; use tcp:// or https:// for remote daemons.",
            nameof(config));

      var pipeName = ExtractNamedPipeName(uri);
      var handler = new SocketsHttpHandler
      {
        ConnectCallback = async (_, ct) =>
        {
          var pipe = new NamedPipeClientStream(
                      ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
          try
          {
            await pipe.ConnectAsync((int)config.ConnectionTimeout.TotalMilliseconds, ct).ConfigureAwait(false);
            return pipe;
          }
          catch
          {
            pipe.Dispose();
            throw;
          }
        },
        ConnectTimeout = config.ConnectionTimeout
      };

      return (handler, "http://localhost");
    }

    private static string ExtractNamedPipeName(Uri uri)
    {
      var path = uri.AbsolutePath.Trim('/');
      if (path.StartsWith("./", StringComparison.Ordinal))
        path = path[2..];
      if (path.StartsWith("pipe/", StringComparison.OrdinalIgnoreCase))
        path = path["pipe/".Length..];
      path = path.Trim('/');
      if (string.IsNullOrEmpty(path))
        throw new ArgumentException("Named pipe URI must include a pipe name.", nameof(uri));
      return Uri.UnescapeDataString(path).Replace('/', '\\');
    }
  }
}
