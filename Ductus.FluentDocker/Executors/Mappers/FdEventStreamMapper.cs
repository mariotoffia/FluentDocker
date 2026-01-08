using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model.Events;
using Ductus.FluentDocker.Model.Networks;
using Newtonsoft.Json.Linq;

namespace Ductus.FluentDocker.Executors.Mappers
{
  public sealed class FdEventStreamMapper : IStreamMapper<FdEvent>
  {
    public string Error { get; private set; } = string.Empty;

    public FdEvent OnData(string data, bool isStdErr)
    {
      if (string.IsNullOrWhiteSpace(data))
        return null;

      try
      {
        return Create(JObject.Parse(data));
      }
      catch (Exception ex) when (ex is Newtonsoft.Json.JsonReaderException || 
                                  ex is ArgumentNullException ||
                                  ex is NullReferenceException ||
                                  ex is FormatException)
      {
        // Skip events that cannot be parsed or have unexpected format
        // This handles differences between Docker and Podman event formats
        return null;
      }
    }

    public FdEvent OnProcessEnd(int exitCode)
    {
      if (exitCode != 0)
        Error = $"Process exited with exit code {exitCode}";

      return null;
    }

    private static FdEvent Create(JObject obj)
    {
      if (null == obj)
        return null;

      // Get header - use null-safe access for compatibility with different container engines
      var action = obj["Action"]?.Value<string>() ?? obj["action"]?.Value<string>();
      var type = obj["Type"]?.Value<string>() ?? obj["type"]?.Value<string>();
      var scope = obj["scope"]?.Value<string>() ?? "local";
      var actor = obj["Actor"] as JObject ?? obj["actor"] as JObject;
      var attributes = actor?["Attributes"] as JObject ?? actor?["attributes"] as JObject ?? new JObject();
      var id = actor?["ID"]?.Value<string>() ?? actor?["id"]?.Value<string>() ?? string.Empty;
      var timeNano = obj["timeNano"]?.Value<long>() ?? obj["timenano"]?.Value<long>() ?? 0;

      // Return null if we couldn't parse essential fields
      if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(type))
        return null;

      var ts = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc).AddTicks(timeNano / 100).ToLocalTime();

      FdEvent evt = null;
      switch (type)
      {
        case "image":
          evt = CreateImageEvent(action, scope, id, ts, attributes);
          break;
        case "container":
          evt = CreateContainerEvent(action, scope, id, ts, attributes);
          break;
        case "network":
          evt = CreateNetworkEvent(action, scope, id, ts, attributes);
          break;
      }

      if (null == evt)
        evt = CreateUnknownEvent(type, action, scope, id, ts, attributes);

      return evt;
    }

    private static FdEvent CreateContainerEvent(string action, string scope, string id, DateTime ts, JObject attributes)
    {
      switch (action)
      {
        case "start":
          return new ContainerStartEvent
          {
            Scope = EventScope.Local,
            Time = ts,
            EventActor = new ContainerStartEvent.ContainerStartActor
            {
              Id = id,
              Name = GetString(attributes, "name"),
              Image = GetString(attributes, "image"),
              Labels = GetExtraInfo(attributes, new[] { "name", "image" })
            }
          };
        case "create":
          return new ContainerCreateEvent
          {
            Scope = EventScope.Local,
            Time = ts,
            EventActor = new ContainerCreateEvent.ContainerCreateActor
            {
              Id = id,
              Name = GetString(attributes, "name"),
              Image = GetString(attributes, "image"),
              Labels = GetExtraInfo(attributes, new[] { "name", "image" })
            }
          };
        case "kill":
          return new ContainerKillEvent
          {
            Scope = EventScope.Local,
            Time = ts,
            EventActor = new ContainerKillEvent.ContainerKillActor
            {
              Id = id,
              Name = GetString(attributes, "name"),
              Image = GetString(attributes, "image"),
              Signal = GetString(attributes, "signal"),
              Labels = GetExtraInfo(attributes, new[] { "name", "image", "signal" })
            }
          };
        case "die":
          return new ContainerDieEvent
          {
            Scope = EventScope.Local,
            Time = ts,
            EventActor = new ContainerDieEvent.ContainerDieActor
            {
              Id = id,
              Name = GetString(attributes, "name"),
              Image = GetString(attributes, "image"),
              ExitCode = GetString(attributes, "exitCode"),
              Labels = GetExtraInfo(attributes, new[] { "name", "image", "exitCode" })
            }
          };
        case "stop":
          return new ContainerStopEvent
          {
            Scope = EventScope.Local,
            Time = ts,
            EventActor = new ContainerStopEvent.ContainerStopActor
            {
              Id = id,
              Name = GetString(attributes, "name"),
              Image = GetString(attributes, "image"),
              Labels = GetExtraInfo(attributes, new[] { "name", "image" })
            }
          };
        case "destroy":
          return new ContainerDestroyEvent
          {
            Scope = EventScope.Local,
            Time = ts,
            EventActor = new ContainerDestroyEvent.ContainerDestroyActor
            {
              Id = id,
              Name = GetString(attributes, "name"),
              Image = GetString(attributes, "image"),
              Labels = GetExtraInfo(attributes, new[] { "name", "image" })
            }
          };
      }

      return null;
    }

    private static FdEvent CreateNetworkEvent(string action, string scope, string id, DateTime ts, JObject attributes)
    {
      switch (action)
      {
        case "connect":
          var connectType = GetString(attributes, "type");
          return new NetworkConnectEvent
          {
            Scope = EventScope.Local,
            Time = ts,
            EventActor = new NetworkConnectEvent.NetworkConnectActor
            {
              Id = id,
              ContainerId = GetString(attributes, "container"),
              Name = GetString(attributes, "name"),
              Type = ParseNetworkType(connectType),
              Labels = GetExtraInfo(attributes, new[] { "container", "name", "type" })
            }
          };
        case "disconnect":
          var disconnectType = GetString(attributes, "type");
          return new NetworkDisconnectEvent
          {
            Scope = EventScope.Local,
            Time = ts,
            EventActor = new NetworkDisconnectEvent.NetworkDisconnectActor
            {
              Id = id,
              ContainerId = GetString(attributes, "container"),
              Name = GetString(attributes, "name"),
              Type = ParseNetworkType(disconnectType),
              Labels = GetExtraInfo(attributes, new[] { "container", "name", "type" })
            }
          };
      }

      return null;
    }

    private static NetworkType ParseNetworkType(string type)
    {
      if (string.IsNullOrEmpty(type))
        return NetworkType.Bridge;
      
      return Enum.TryParse<NetworkType>(type, true, out var result) ? result : NetworkType.Bridge;
    }

    private static FdEvent CreateImageEvent(string action, string scope, string id, DateTime ts, JObject attributes)
    {
      switch (action)
      {
        case "pull":
          return new ImagePullEvent
          {
            Scope = EventScope.Local,
            Time = ts,
            EventActor = new ImagePullEvent.ImagePullActor
            {
              Id = id,
              Name = GetString(attributes, "name"),
              Labels = GetExtraInfo(attributes, new[] { "name" })
            }
          };
      }

      return null;
    }

    private static FdEvent CreateUnknownEvent(string type, string action, string scope, string id, DateTime ts, JObject attributes)
    {
      return new UnknownEvent(action, type)
      {
        Scope = EventScope.Local,
        Time = ts,
        EventActor = new UnknownEvent.UnknownActor
        {
          Id = id,
          Labels = new List<Tuple<string, string>>(),
          Attributes = GetExtraInfo(attributes, Array.Empty<string>())
        }
      };
    }

    private static IList<Tuple<string, string>> GetExtraInfo(JObject obj, string[] nonlabels)
    {
      var list = new List<Tuple<string, string>>();
      if (obj == null)
        return list;
        
      foreach (var prop in obj.Properties().Where(x => !nonlabels.Contains(x.Name)))
      {
        list.Add(new Tuple<string, string>(prop.Name, prop.Value?.ToString() ?? string.Empty));
      }
      return list;
    }

    /// <summary>
    /// Safely gets a string value from a JObject, returning empty string if not found.
    /// </summary>
    private static string GetString(JObject obj, string key)
    {
      return obj?[key]?.Value<string>() ?? string.Empty;
    }
  }
}
