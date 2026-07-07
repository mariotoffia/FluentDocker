using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  internal static class DockerApiRegistryAuth
  {
    private const string DockerHubServer = "https://index.docker.io/v1/";
    private static readonly ConditionalWeakTable<IDockerApiConnection, AuthCache> Caches = new();

    /// <inheritdoc />
    public static void Store(IDockerApiConnection connection, RegistryLoginConfig config)
    {
      var cache = Caches.GetValue(connection, _ => new AuthCache());
      cache.Store(Normalize(config.Server ?? DockerHubServer), config);
    }

    /// <inheritdoc />
    public static void Remove(IDockerApiConnection connection, string server)
    {
      if (!Caches.TryGetValue(connection, out var cache))
        return;
      if (server == null)
        cache.Clear();
      else
        cache.Remove(Normalize(server));
    }

    /// <inheritdoc />
    public static void Clear(IDockerApiConnection connection)
    {
      Caches.Remove(connection);
    }

    /// <inheritdoc />
    public static IReadOnlyDictionary<string, string> HeaderFor(
        IDockerApiConnection connection, string image)
    {
      if (!Caches.TryGetValue(connection, out var cache))
        return null;

      var server = Normalize(RegistryFromImage(image));
      var config = cache.Get(server);
      if (config == null)
        return null;

      var json = JsonHelper.Serialize(new
      {
        username = config.Username,
        password = config.Password,
        email = config.Email,
        serveraddress = config.Server ?? DockerHubServer
      });
      return new Dictionary<string, string>
      {
        ["X-Registry-Auth"] = ToBase64Url(json)
      };
    }

    /// <inheritdoc />
    public static IReadOnlyDictionary<string, string> RegistryConfigHeaderFor(
        IDockerApiConnection connection)
    {
      if (!Caches.TryGetValue(connection, out var cache))
        return null;

      var configs = cache.Snapshot();
      if (configs.Count == 0)
        return null;

      return new Dictionary<string, string>
      {
        ["X-Registry-Config"] = ToBase64Url(JsonHelper.Serialize(configs))
      };
    }

    private static string RegistryFromImage(string image)
    {
      var slash = image?.IndexOf('/') ?? -1;
      if (slash < 0)
        return DockerHubServer;
      var first = image[..slash];
      return first != null &&
          (first.Contains('.') || first.Contains(':') || first == "localhost")
          ? first
          : DockerHubServer;
    }

    private static string Normalize(string server)
    {
      if (string.IsNullOrWhiteSpace(server))
        return Normalize(DockerHubServer);

      var value = server.Trim().TrimEnd('/');
      if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        value = value["https://".Length..];
      else if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        value = value["http://".Length..];
      return value.Equals("index.docker.io/v1", StringComparison.OrdinalIgnoreCase) ||
          value.Equals("index.docker.io", StringComparison.OrdinalIgnoreCase) ||
          value.Equals("registry-1.docker.io", StringComparison.OrdinalIgnoreCase)
          ? "docker.io"
          : value.ToLowerInvariant();
    }

    private static string ToBase64Url(string value)
    {
      return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
          .Replace('+', '-').Replace('/', '_');
    }

    private sealed class AuthCache
    {
      private readonly Dictionary<string, RegistryLoginConfig> _configs = [];

      /// <inheritdoc />
      public void Store(string server, RegistryLoginConfig config)
      {
        lock (_configs)
          _configs[server] = config;
      }

      /// <inheritdoc />
      public RegistryLoginConfig Get(string server)
      {
        lock (_configs)
          return _configs.TryGetValue(server, out var config) ? config : null;
      }

      /// <inheritdoc />
      public Dictionary<string, object> Snapshot()
      {
        lock (_configs)
        {
          var result = new Dictionary<string, object>();
          foreach (var kv in _configs)
          {
            result[kv.Key] = new
            {
              username = kv.Value.Username,
              password = kv.Value.Password,
              email = kv.Value.Email,
              serveraddress = kv.Value.Server ?? DockerHubServer
            };
          }
          return result;
        }
      }

      /// <inheritdoc />
      public void Remove(string server)
      {
        lock (_configs)
          _configs.Remove(server);
      }

      /// <inheritdoc />
      public void Clear()
      {
        lock (_configs)
          _configs.Clear();
      }
    }
  }
}
