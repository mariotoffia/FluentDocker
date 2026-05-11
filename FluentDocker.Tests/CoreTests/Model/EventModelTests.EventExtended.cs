using System;
using FluentDocker.Model.Events;
using FluentDocker.Model.Networks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public partial class EventModelTests
  {
    #region ImagePullEvent

    [Fact]
    public void ImagePullEvent_Constructor_SetsActionAndType()
    {
      var evt = new ImagePullEvent();

      Assert.Equal(EventAction.Pull, evt.Action);
      Assert.Equal(EventType.Image, evt.Type);
    }

    [Fact]
    public void ImagePullEvent_Actor_NameCanBeSet()
    {
      var actor = new ImagePullEvent.ImagePullActor { Name = "alpine" };

      var evt = new ImagePullEvent { EventActor = actor };

      Assert.Equal("alpine", evt.EventActor.Name);
    }

    [Fact]
    public void ImagePullEvent_FullConstruction()
    {
      var actor = new ImagePullEvent.ImagePullActor { Name = "nginx" };

      var evt = new ImagePullEvent
      {
        Scope = EventScope.Local,
        Time = new DateTime(2026, 3, 27, 8, 0, 0, DateTimeKind.Utc),
        EventActor = actor
      };

      Assert.Equal(EventAction.Pull, evt.Action);
      Assert.Equal(EventType.Image, evt.Type);
      Assert.Equal(EventScope.Local, evt.Scope);
      Assert.Equal("nginx", evt.EventActor.Name);
    }

    #endregion

    #region NetworkConnectEvent

    [Fact]
    public void NetworkConnectEvent_Constructor_SetsActionAndType()
    {
      var evt = new NetworkConnectEvent();

      Assert.Equal(EventAction.Connect, evt.Action);
      Assert.Equal(EventType.Network, evt.Type);
    }

    [Fact]
    public void NetworkConnectEvent_Actor_AllPropertiesCanBeSet()
    {
      var actor = new NetworkConnectEvent.NetworkConnectActor
      {
        ContainerId = "abc123def456",
        Name = "my-bridge-network",
        Type = NetworkType.Bridge,
        CustomType = null
      };

      var evt = new NetworkConnectEvent { EventActor = actor };

      Assert.Equal("abc123def456", evt.EventActor.ContainerId);
      Assert.Equal("my-bridge-network", evt.EventActor.Name);
      Assert.Equal(NetworkType.Bridge, evt.EventActor.Type);
      Assert.Null(evt.EventActor.CustomType);
    }

    [Fact]
    public void NetworkConnectEvent_Actor_CustomNetworkType()
    {
      var actor = new NetworkConnectEvent.NetworkConnectActor
      {
        ContainerId = "container-hash-789",
        Name = "custom-net",
        Type = NetworkType.Custom,
        CustomType = "weave"
      };

      Assert.Equal(NetworkType.Custom, actor.Type);
      Assert.Equal("weave", actor.CustomType);
    }

    [Theory]
    [InlineData(NetworkType.Bridge)]
    [InlineData(NetworkType.Host)]
    [InlineData(NetworkType.Overlay)]
    [InlineData(NetworkType.Ipvlan)]
    [InlineData(NetworkType.Macvlan)]
    [InlineData(NetworkType.None)]
    [InlineData(NetworkType.Custom)]
    public void NetworkConnectEvent_Actor_AcceptsAllNetworkTypes(NetworkType networkType)
    {
      var actor = new NetworkConnectEvent.NetworkConnectActor { Type = networkType };
      Assert.Equal(networkType, actor.Type);
    }

    [Fact]
    public void NetworkConnectEvent_Actor_DefaultNetworkType_IsUnknown()
    {
      var actor = new NetworkConnectEvent.NetworkConnectActor();
      Assert.Equal(NetworkType.Unknown, actor.Type);
    }

    #endregion

    #region NetworkDisconnectEvent

    [Fact]
    public void NetworkDisconnectEvent_Constructor_SetsActionAndType()
    {
      var evt = new NetworkDisconnectEvent();

      Assert.Equal(EventAction.Disconnect, evt.Action);
      Assert.Equal(EventType.Network, evt.Type);
    }

    [Fact]
    public void NetworkDisconnectEvent_Actor_AllPropertiesCanBeSet()
    {
      var actor = new NetworkDisconnectEvent.NetworkDisconnectActor
      {
        ContainerId = "xyz789",
        Name = "backend-network",
        Type = NetworkType.Overlay,
        CustomType = null
      };

      var evt = new NetworkDisconnectEvent { EventActor = actor };

      Assert.Equal("xyz789", evt.EventActor.ContainerId);
      Assert.Equal("backend-network", evt.EventActor.Name);
      Assert.Equal(NetworkType.Overlay, evt.EventActor.Type);
      Assert.Null(evt.EventActor.CustomType);
    }

    [Fact]
    public void NetworkDisconnectEvent_Actor_CustomNetworkType()
    {
      var actor = new NetworkDisconnectEvent.NetworkDisconnectActor
      {
        ContainerId = "container-aaa",
        Name = "calico-net",
        Type = NetworkType.Custom,
        CustomType = "calico"
      };

      Assert.Equal(NetworkType.Custom, actor.Type);
      Assert.Equal("calico", actor.CustomType);
    }

    [Fact]
    public void NetworkDisconnectEvent_Actor_DefaultNetworkType_IsUnknown()
    {
      var actor = new NetworkDisconnectEvent.NetworkDisconnectActor();
      Assert.Equal(NetworkType.Unknown, actor.Type);
    }

    #endregion

    #region UnknownEvent

    [Fact]
    public void UnknownEvent_KnownActionAndType_ParsesCorrectly()
    {
      var evt = new UnknownEvent("Create", "Container");

      Assert.Equal(EventAction.Create, evt.Action);
      Assert.Equal(EventType.Container, evt.Type);
      Assert.Equal("Create", evt.ActionRaw);
      Assert.Equal("Container", evt.TypeRaw);
    }

    [Fact]
    public void UnknownEvent_UnknownAction_FallsBackToUnspecified()
    {
      var evt = new UnknownEvent("SomeNewAction", "Container");

      Assert.Equal(EventAction.Unspecified, evt.Action);
      Assert.Equal(EventType.Container, evt.Type);
      Assert.Equal("SomeNewAction", evt.ActionRaw);
      Assert.Equal("Container", evt.TypeRaw);
    }

    [Fact]
    public void UnknownEvent_UnknownType_FallsBackToGeneric()
    {
      var evt = new UnknownEvent("Pull", "SomeNewType");

      Assert.Equal(EventAction.Pull, evt.Action);
      Assert.Equal(EventType.Generic, evt.Type);
      Assert.Equal("Pull", evt.ActionRaw);
      Assert.Equal("SomeNewType", evt.TypeRaw);
    }

    [Fact]
    public void UnknownEvent_BothUnknown_FallsBackToDefaults()
    {
      var evt = new UnknownEvent("custom-action", "custom-type");

      Assert.Equal(EventAction.Unspecified, evt.Action);
      Assert.Equal(EventType.Generic, evt.Type);
      Assert.Equal("custom-action", evt.ActionRaw);
      Assert.Equal("custom-type", evt.TypeRaw);
    }

    [Theory]
    [InlineData("Pull", EventAction.Pull)]
    [InlineData("Create", EventAction.Create)]
    [InlineData("Start", EventAction.Start)]
    [InlineData("Kill", EventAction.Kill)]
    [InlineData("Die", EventAction.Die)]
    [InlineData("Connect", EventAction.Connect)]
    [InlineData("Disconnect", EventAction.Disconnect)]
    [InlineData("Stop", EventAction.Stop)]
    [InlineData("Destroy", EventAction.Destroy)]
    public void UnknownEvent_AllKnownActions_ParseCorrectly(
      string rawAction, EventAction expected)
    {
      var evt = new UnknownEvent(rawAction, "Generic");
      Assert.Equal(expected, evt.Action);
    }

    [Theory]
    [InlineData("Generic", EventType.Generic)]
    [InlineData("Image", EventType.Image)]
    [InlineData("Container", EventType.Container)]
    [InlineData("Network", EventType.Network)]
    [InlineData("Plugin", EventType.Plugin)]
    [InlineData("Volume", EventType.Volume)]
    [InlineData("Daemon", EventType.Daemon)]
    [InlineData("Service", EventType.Service)]
    [InlineData("Node", EventType.Node)]
    [InlineData("Secret", EventType.Secret)]
    [InlineData("Config", EventType.Config)]
    public void UnknownEvent_AllKnownTypes_ParseCorrectly(
      string rawType, EventType expected)
    {
      var evt = new UnknownEvent("Unspecified", rawType);
      Assert.Equal(expected, evt.Type);
    }

    [Fact]
    public void UnknownEvent_RawValues_PreservedRegardlessOfParsing()
    {
      var evt = new UnknownEvent("Create", "Container");

      // Even when parsing succeeds, raw values are preserved
      Assert.Equal("Create", evt.ActionRaw);
      Assert.Equal("Container", evt.TypeRaw);
    }

    [Fact]
    public void UnknownEvent_CaseSensitiveParsing()
    {
      // Enum.TryParse is case-sensitive by default
      var evt = new UnknownEvent("create", "container");

      // Lowercase should fail to parse (case-sensitive)
      Assert.Equal(EventAction.Unspecified, evt.Action);
      Assert.Equal(EventType.Generic, evt.Type);
      Assert.Equal("create", evt.ActionRaw);
      Assert.Equal("container", evt.TypeRaw);
    }

    [Fact]
    public void UnknownEvent_EmptyStrings_FallBackToDefaults()
    {
      var evt = new UnknownEvent("", "");

      Assert.Equal(EventAction.Unspecified, evt.Action);
      Assert.Equal(EventType.Generic, evt.Type);
      Assert.Equal("", evt.ActionRaw);
      Assert.Equal("", evt.TypeRaw);
    }

    [Fact]
    public void UnknownEvent_IsAssignableToFdEvent()
    {
      var evt = new UnknownEvent("Pull", "Image");

      Assert.IsAssignableFrom<FdEvent>(evt);
    }

    [Fact]
    public void UnknownEvent_Actor_DefaultProperties_AreNull()
    {
      var actor = new UnknownEvent.UnknownActor();

      Assert.Null(actor.Id);
      Assert.Null(actor.Labels);
      Assert.Null(actor.Attributes);
    }

    #endregion

    #region Inheritance and Polymorphism

    [Fact]
    public void AllContainerEvents_ShareContainerType()
    {
      FdEvent[] events =
      [
        new ContainerCreateEvent(),
        new ContainerStartEvent(),
        new ContainerStopEvent(),
        new ContainerDieEvent(),
        new ContainerDestroyEvent(),
        new ContainerKillEvent()
      ];

      foreach (var evt in events)
      {
        Assert.Equal(EventType.Container, evt.Type);
      }
    }

    [Fact]
    public void AllEvents_AreAssignableToFdEvent()
    {
      FdEvent[] events =
      [
        new ContainerCreateEvent(),
        new ContainerStartEvent(),
        new ContainerStopEvent(),
        new ContainerDieEvent(),
        new ContainerDestroyEvent(),
        new ContainerKillEvent(),
        new ImagePullEvent(),
        new NetworkConnectEvent(),
        new NetworkDisconnectEvent(),
        new UnknownEvent("test", "test")
      ];

      Assert.Equal(10, events.Length);
      foreach (var evt in events)
      {
        Assert.IsAssignableFrom<FdEvent>(evt);
      }
    }

    [Fact]
    public void EachConcreteEvent_HasDistinctAction()
    {
      var create = new ContainerCreateEvent();
      var start = new ContainerStartEvent();
      var stop = new ContainerStopEvent();
      var die = new ContainerDieEvent();
      var destroy = new ContainerDestroyEvent();
      var kill = new ContainerKillEvent();
      var pull = new ImagePullEvent();
      var connect = new NetworkConnectEvent();
      var disconnect = new NetworkDisconnectEvent();

      Assert.Equal(EventAction.Create, create.Action);
      Assert.Equal(EventAction.Start, start.Action);
      Assert.Equal(EventAction.Stop, stop.Action);
      Assert.Equal(EventAction.Die, die.Action);
      Assert.Equal(EventAction.Destroy, destroy.Action);
      Assert.Equal(EventAction.Kill, kill.Action);
      Assert.Equal(EventAction.Pull, pull.Action);
      Assert.Equal(EventAction.Connect, connect.Action);
      Assert.Equal(EventAction.Disconnect, disconnect.Action);
    }

    [Fact]
    public void ContainerEventActors_InheritFromEventActor()
    {
      var actors = new EventActor[]
      {
        new ContainerCreateEvent.ContainerCreateActor(),
        new ContainerStartEvent.ContainerStartActor(),
        new ContainerStopEvent.ContainerStopActor(),
        new ContainerDieEvent.ContainerDieActor(),
        new ContainerDestroyEvent.ContainerDestroyActor(),
        new ContainerKillEvent.ContainerKillActor(),
        new ImagePullEvent.ImagePullActor(),
        new NetworkConnectEvent.NetworkConnectActor(),
        new NetworkDisconnectEvent.NetworkDisconnectActor(),
        new UnknownEvent.UnknownActor()
      };

      Assert.Equal(10, actors.Length);
      foreach (var actor in actors)
      {
        Assert.IsAssignableFrom<EventActor>(actor);
      }
    }

    #endregion
  }
}
