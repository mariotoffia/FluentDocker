using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using FluentDocker.Common;
using FluentDocker.Drivers.Docker.Api.Connection;
using FluentDocker.Model.Drivers;

namespace FluentDocker.Drivers.Docker.Api.Components
{
  /// <summary>Builds Docker registry authentication headers.</summary>
  /// <remarks>X-Registry-Auth credentials are held in memory for the connection lifetime and never logged, so they may still be visible in memory dumps.</remarks>
  internal static class DockerApiRegistryAuth
  {
    private const string DockerHubServer = "https://index.docker.io/v1/";
    private static readonly ConditionalWeakTable<IDockerApiConnection, AuthCache> Caches = new();

    /// <summary>Stores a successful registry login for later pull, push, and build requests.</summary>
    public static void Store(IDockerApiConnection connection, RegistryLoginConfig config)
    {
      var cache = Caches.GetValue(connection, _ => new AuthCache());
      cache.Store(Normalize(config.Server ?? DockerHubServer), config);
    }

    /// <summary>Removes cached credentials for one registry, or all credentials when server is null.</summary>
    public static void Remove(IDockerApiConnection connection, string server)
    {
      if (!Caches.TryGetValue(connection, out var cache))
        return;
      if (server == null)
        cache.Clear();
      else
        cache.Remove(Normalize(server));
    }

    /// <summary>Clears all cached registry credentials for the connection.</summary>
    public static void Clear(IDockerApiConnection connection)
    {
      Caches.Remove(connection);
    }

    /// <summary>Builds the single-image Docker auth header for pull and push requests.</summary>
    public static IReadOnlyDictionary<string, string> HeaderFor(
        IDockerApiConnection connection, string image)
    {
      if (!Caches.TryGetValue(connection, out var cache))
        return null;

      var server = Normalize(RegistryFromImage(image));
      var config = cache.Get(server);
      if (config == null)
        return null;

      var json = JsonHelper.Serialize(AuthConfig(config, server));
      return new Dictionary<string, string>
      {
        ["X-Registry-Auth"] = ToBase64Url(json)
      };
    }

    /// <summary>Builds Docker's registry auth-config map for image build requests.</summary>
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
      return IsDockerHub(value)
          ? DockerHubServer
          : value.ToLowerInvariant();
    }

    private static bool IsDockerHub(string value)
    {
      var server = value.Trim().TrimEnd('/');
      if (server.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        server = server["https://".Length..];
      else if (server.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        server = server["http://".Length..];
      return server.Equals("index.docker.io/v1", StringComparison.OrdinalIgnoreCase) ||
          server.Equals("index.docker.io", StringComparison.OrdinalIgnoreCase) ||
          server.Equals("registry-1.docker.io", StringComparison.OrdinalIgnoreCase) ||
          server.Equals("docker.io", StringComparison.OrdinalIgnoreCase);
    }

    private static object AuthConfig(RegistryLoginConfig config, string server) => new
    {
      username = config.Username,
      password = config.Password,
      email = config.Email,
      serveraddress = IsDockerHub(server) ? DockerHubServer : config.Server ?? server
    };

    private static string ToBase64Url(string value)
    {
      return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
          .Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Per-connection registry credential cache.</summary>
    /// <remarks>Credentials stay in memory until the connection is cleared or collected; they are never logged but may appear in memory dumps.</remarks>
    private sealed class AuthCache
    {
      private readonly Dictionary<string, RegistryLoginConfig> _configs = [];

      /// <summary>Stores credentials under a normalized registry lookup key.</summary>
      public void Store(string server, RegistryLoginConfig config)
      {
        lock (_configs)
          _configs[server] = config;
      }

      /// <summary>Gets credentials for a normalized registry lookup key.</summary>
      public RegistryLoginConfig Get(string server)
      {
        lock (_configs)
          return _configs.TryGetValue(server, out var config) ? config : null;
      }

      /// <summary>Returns Docker auth-config JSON objects keyed by daemon registry address.</summary>
      public Dictionary<string, object> Snapshot()
      {
        lock (_configs)
        {
          var result = new Dictionary<string, object>();
          foreach (var kv in _configs)
          {
            result[kv.Key] = AuthConfig(kv.Value, kv.Key);
          }
          return result;
        }
      }

      /// <summary>Removes credentials for a normalized registry lookup key.</summary>
      public void Remove(string server)
      {
        lock (_configs)
          _configs.Remove(server);
      }

      /// <summary>Clears every cached registry login.</summary>
      public void Clear()
      {
        lock (_configs)
          _configs.Clear();
      }
    }
  }
}
