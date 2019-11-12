using System;
using System.Collections.Generic;
using System.Linq;
using Ductus.FluentDocker.Model.Events;
using Newtonsoft.Json.Linq;

namespace Ductus.FluentDocker.Executors.Mappers
{
  public sealed class FdEventStreamMapper : IStreamMapper<FdEvent>
  {
    public string Error { get; private set; } = string.Empty;

    public FdEvent OnData(string data, bool isStdErr)
    {
      if (null == data)
        return null;

      return Create(JObject.Parse(data));
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

      // Get header
      var action = obj["Action"].Value<string>();
      var type = obj["Type"].Value<string>();
      var scope = obj["scope"].Value<string>();
      var actor = (JObject)obj["Actor"];
      var attributes = (JObject)actor["Attributes"];
      var id = actor["ID"].Value<string>();
      var timeNano = obj["timeNano"].Value<long>();

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
      }

      // TODO: if evt is null -> render default event
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
              Name = attributes["name"].Value<string>(),
              Image = attributes["image"].Value<string>(),
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
              Name = attributes["name"].Value<string>(),
              Image = attributes["image"].Value<string>(),
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
              Name = attributes["name"].Value<string>(),
              Image = attributes["image"].Value<string>(),
              Signal = attributes["signal"].Value<string>(),
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
              Name = attributes["name"].Value<string>(),
              Image = attributes["image"].Value<string>(),
              ExitCode = attributes["exitCode"].Value<string>(),
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
              Name = attributes["name"].Value<string>(),
              Image = attributes["image"].Value<string>(),
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
              Name = attributes["name"].Value<string>(),
              Image = attributes["image"].Value<string>(),
              Labels = GetExtraInfo(attributes, new[] { "name", "image" })
            }
          };
      }

      return null;
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
              Name = attributes["name"].Value<string>(),
              Labels = GetExtraInfo(attributes, new[] { "name" })
            }
          };
      }

      return null;
    }

    private static IList<Tuple<string, string>> GetExtraInfo(JObject obj, string[] nonlabels)
    {
      var list = new List<Tuple<string, string>>();
      foreach (var prop in obj.Properties().Where(x => !nonlabels.Contains(x.Name)))
      {
        list.Add(new Tuple<string, string>(prop.Name, prop.Value<string>()));
      }
      return list;
    }
  }
}
