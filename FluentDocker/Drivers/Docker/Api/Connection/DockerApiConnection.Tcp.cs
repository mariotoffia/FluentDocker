using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using FluentDocker.Drivers.Models.Connection;

namespace FluentDocker.Drivers.Docker.Api.Connection
{
  public sealed partial class DockerApiConnection
  {
    private bool UseLongRunningPostClient(string path) =>
        IsWaitEndpoint(path) || StopOrRestartTimeoutExceedsRequestTimeout(path);

    private static bool IsWaitEndpoint(string path)
    {
      var queryStart = path.IndexOf('?');
      var pathOnly = queryStart < 0 ? path : path[..queryStart];
      return pathOnly.Contains("/containers/", StringComparison.Ordinal) &&
          pathOnly.EndsWith("/wait", StringComparison.Ordinal);
    }

    private bool StopOrRestartTimeoutExceedsRequestTimeout(string path)
    {
      if (!path.Contains("/containers/", StringComparison.Ordinal) ||
          (!path.Contains("/stop?", StringComparison.Ordinal) &&
           !path.Contains("/restart?", StringComparison.Ordinal)))
        return false;

      var seconds = QueryInt32(path, "t");
      // t < 0 means "wait indefinitely" to the daemon — always long-running.
      return seconds.HasValue &&
          (seconds.Value < 0 || TimeSpan.FromSeconds(seconds.Value) >= _config.RequestTimeout);
    }

    private static int? QueryInt32(string path, string name)
    {
      var queryStart = path.IndexOf('?');
      if (queryStart < 0 || queryStart == path.Length - 1)
        return null;

      var query = path[(queryStart + 1)..];
      foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
      {
        var equals = part.IndexOf('=');
        var key = equals < 0 ? part : part[..equals];
        if (!string.Equals(key, name, StringComparison.Ordinal))
          continue;

        var value = equals < 0 ? string.Empty : part[(equals + 1)..];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
      }
      return null;
    }

    // Every X509Certificate2 created here is added to ownedCertificates so the connection
    // instance can dispose them (they own native handles); they outlive this method because
    // the client cert is referenced by the handler and the CA cert is captured by the
    // validation callback below.
    private static (SocketsHttpHandler, string) CreateTcpHandler(
        Uri uri, DockerApiConnectionConfig config, bool useTls, List<X509Certificate2> ownedCertificates)
    {
      var handler = new SocketsHttpHandler
      {
        ConnectTimeout = config.ConnectionTimeout,
        ConnectCallback = async (context, ct) =>
        {
          var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
          try
          {
            EnableKeepAlive(socket);
            await socket.ConnectAsync(context.DnsEndPoint, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
          }
          catch
          {
            socket.Dispose();
            throw;
          }
        }
      };

      var hasCerts = useTls && !string.IsNullOrEmpty(config.CertificatePath);
      ConfigureTls(handler, config, useTls, hasCerts, ownedCertificates);

      var scheme = useTls ? "https" : "http";
      var port = ResolveDockerPort(uri, useTls);
      var baseAddress = $"{scheme}://{uri.Host}:{port}";

      return (handler, baseAddress);
    }

    private static int ResolveDockerPort(Uri uri, bool useTls)
    {
      return uri.Port > 0 ? uri.Port : (useTls ? 2376 : 2375);
    }

    private static void EnableKeepAlive(Socket socket)
    {
      socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
      TrySetTcpKeepAlive(socket, SocketOptionName.TcpKeepAliveTime, 30);
      TrySetTcpKeepAlive(socket, SocketOptionName.TcpKeepAliveInterval, 10);
      TrySetTcpKeepAlive(socket, SocketOptionName.TcpKeepAliveRetryCount, 5);
    }

    private static void TrySetTcpKeepAlive(Socket socket, SocketOptionName option, int value)
    {
      try
      {
        socket.SetSocketOption(SocketOptionLevel.Tcp, option, value);
      }
      catch (Exception ex) when (ex is SocketException or PlatformNotSupportedException)
      {
      }
    }

    private static void ConfigureTls(
        SocketsHttpHandler handler, DockerApiConnectionConfig config, bool useTls,
        bool hasCerts, List<X509Certificate2> ownedCertificates)
    {
      if (!useTls && !hasCerts)
        return;

      var sslOptions = new SslClientAuthenticationOptions();
      if (hasCerts)
        ConfigureCertificateTls(sslOptions, config, ownedCertificates);
      else if (!config.VerifyTls)
      {
#pragma warning disable CA5359 // Intentional: user opted out of TLS verification via VerifyTls=false
        sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
      }
      else if (config.AllowTlsHostnameMismatch)
      {
        sslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) =>
            errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateNameMismatch;
      }

      handler.SslOptions = sslOptions;
    }

    private static void ConfigureCertificateTls(
        SslClientAuthenticationOptions sslOptions, DockerApiConnectionConfig config,
        List<X509Certificate2> ownedCertificates)
    {
      ValidateCertificatePath(config);
      var certPath = Path.Combine(config.CertificatePath, "cert.pem");
      var keyPath = Path.Combine(config.CertificatePath, "key.pem");

      if (File.Exists(certPath) && File.Exists(keyPath))
      {
        var clientCert = ClientCertificateLoader.Load(certPath, keyPath);
        ownedCertificates.Add(clientCert);
        sslOptions.ClientCertificates = [clientCert];
      }

      if (!config.VerifyTls)
      {
#pragma warning disable CA5359 // Intentional: user opted out of TLS verification via VerifyTls=false
        sslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
#pragma warning restore CA5359
        return;
      }

      var caPath = Path.Combine(config.CertificatePath, "ca.pem");
      if (File.Exists(caPath))
      {
#if NET9_0_OR_GREATER
        var caCert = X509CertificateLoader.LoadCertificateFromFile(caPath);
#else
        var caCert = X509Certificate2.CreateFromPem(File.ReadAllText(caPath));
#endif
        ownedCertificates.Add(caCert);
        sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
            ModelTlsValidation.ValidateWithCustomRoot(
                caCert, cert, chain, errors, config.AllowTlsHostnameMismatch);
      }
      else if (config.AllowTlsHostnameMismatch)
      {
        sslOptions.RemoteCertificateValidationCallback = (_, _, _, errors) =>
            errors is SslPolicyErrors.None or SslPolicyErrors.RemoteCertificateNameMismatch;
      }
    }

    private static void ValidateCertificatePath(DockerApiConnectionConfig config)
    {
      if (!Directory.Exists(config.CertificatePath))
        throw new InvalidOperationException(
            $"DockerApiConnectionConfig.CertificatePath '{config.CertificatePath}' does not exist.");

      var certPath = Path.Combine(config.CertificatePath, "cert.pem");
      var keyPath = Path.Combine(config.CertificatePath, "key.pem");
      var caPath = Path.Combine(config.CertificatePath, "ca.pem");
      var hasCert = File.Exists(certPath);
      var hasKey = File.Exists(keyPath);
      var hasCa = File.Exists(caPath);
      if (hasCert != hasKey)
        RequireCertificateFile(hasCert ? keyPath : certPath,
            hasCert ? "client private key" : "client certificate");
      if (!hasCert && !hasKey && !hasCa)
        throw new InvalidOperationException(
            $"Configured certificate directory '{config.CertificatePath}' contains none of " +
            "cert.pem, key.pem or ca.pem; check the path.");
    }

    private static void RequireCertificateFile(string path, string description)
    {
      if (!File.Exists(path))
        throw new InvalidOperationException(
            $"Configured certificate directory is missing {Path.GetFileName(path)} ({description}).");
    }
  }
}
