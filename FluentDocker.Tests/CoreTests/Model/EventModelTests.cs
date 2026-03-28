using System;
using System.Collections.Generic;
using FluentDocker.Model.Events;
using FluentDocker.Model.Networks;
using Xunit;

namespace FluentDocker.Tests.CoreTests.Model
{
  [Trait("Category", "Unit")]
  public partial class EventModelTests
  {
    #region Enum Values

    [Theory]
    [InlineData(nameof(EventType.Generic), 0)]
    [InlineData(nameof(EventType.Image), 1)]
    [InlineData(nameof(EventType.Container), 2)]
    [InlineData(nameof(EventType.Network), 3)]
    [InlineData(nameof(EventType.Plugin), 4)]
    [InlineData(nameof(EventType.Volume), 5)]
    [InlineData(nameof(EventType.Daemon), 6)]
    [InlineData(nameof(EventType.Service), 7)]
    [InlineData(nameof(EventType.Node), 8)]
    [InlineData(nameof(EventType.Secret), 9)]
    [InlineData(nameof(EventType.Config), 10)]
    public void EventType_HasExpectedValues(string name, int value)
    {
      var parsed = Enum.Parse<EventType>(name);
      Assert.Equal(value, (int)parsed);
    }

    [Fact]
    public void EventType_HasExactlyElevenMembers()
    {
      var values = Enum.GetValues<EventType>();
      Assert.Equal(11, values.Length);
    }

    [Theory]
    [InlineData(nameof(EventScope.Unknown), 0)]
    [InlineData(nameof(EventScope.Local), 1)]
    public void EventScope_HasExpectedValues(string name, int value)
    {
      var parsed = Enum.Parse<EventScope>(name);
      Assert.Equal(value, (int)parsed);
    }

    [Fact]
    public void EventScope_HasExactlyTwoMembers()
    {
      var values = Enum.GetValues<EventScope>();
      Assert.Equal(2, values.Length);
    }

    [Theory]
    [InlineData(nameof(EventAction.Unspecified), 0)]
    [InlineData(nameof(EventAction.Pull), 1)]
    [InlineData(nameof(EventAction.Create), 2)]
    [InlineData(nameof(EventAction.Start), 3)]
    [InlineData(nameof(EventAction.Kill), 4)]
    [InlineData(nameof(EventAction.Die), 5)]
    [InlineData(nameof(EventAction.Connect), 6)]
    [InlineData(nameof(EventAction.Disconnect), 7)]
    [InlineData(nameof(EventAction.Stop), 8)]
    [InlineData(nameof(EventAction.Destroy), 9)]
    public void EventAction_HasExpectedValues(string name, int value)
    {
      var parsed = Enum.Parse<EventAction>(name);
      Assert.Equal(value, (int)parsed);
    }

    [Fact]
    public void EventAction_HasExactlyTenMembers()
    {
      var values = Enum.GetValues<EventAction>();
      Assert.Equal(10, values.Length);
    }

    #endregion

    #region EventActor

    [Fact]
    public void EventActor_DefaultProperties_AreNull()
    {
      var actor = new EventActor();

      Assert.Null(actor.Id);
      Assert.Null(actor.Labels);
    }

    #endregion

    #region FdEvent Base Properties (via ContainerCreateEvent)

    [Fact]
    public void FdEvent_DefaultScope_IsUnknown()
    {
      var evt = new ContainerCreateEvent();
      Assert.Equal(EventScope.Unknown, evt.Scope);
    }

    [Fact]
    public void FdEvent_ScopeCanBeSet()
    {
      var evt = new ContainerCreateEvent { Scope = EventScope.Local };
      Assert.Equal(EventScope.Local, evt.Scope);
    }

    [Fact]
    public void FdEvent_TimeCanBeSet()
    {
      var now = new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc);
      var evt = new ContainerCreateEvent { Time = now };
      Assert.Equal(now, evt.Time);
    }

    [Fact]
    public void FdEvent_DefaultTime_IsDateTimeMinValue()
    {
      var evt = new ContainerCreateEvent();
      Assert.Equal(default, evt.Time);
    }

    [Fact]
    public void FdEvent_EventActorCanBeSetViaBaseProperty()
    {
      var actor = new ContainerCreateEvent.ContainerCreateActor
      {
        Image = "alpine:latest",
        Name = "test-container"
      };

      var evt = new ContainerCreateEvent { EventActor = actor };

      Assert.Same(actor, evt.EventActor);
      Assert.Equal("alpine:latest", evt.EventActor.Image);
      Assert.Equal("test-container", evt.EventActor.Name);
    }

    [Fact]
    public void FdEvent_GenericEventActor_IsAccessibleViaBaseType()
    {
      var actor = new ContainerCreateEvent.ContainerCreateActor
      {
        Image = "nginx:1.25",
        Name = "web"
      };

      var evt = new ContainerCreateEvent { EventActor = actor };

      // Access via the base FdEvent property (non-generic)
      FdEvent baseEvent = evt;
      Assert.NotNull(baseEvent.EventActor);
      Assert.IsType<ContainerCreateEvent.ContainerCreateActor>(baseEvent.EventActor);
    }

    [Fact]
    public void FdEvent_GenericEventActor_CastRoundTrip()
    {
      var actor = new ContainerStartEvent.ContainerStartActor
      {
        Image = "redis:7",
        Name = "cache"
      };

      var evt = new ContainerStartEvent { EventActor = actor };

      // Base property returns EventActor, generic property returns typed actor
      FdEvent baseEvent = evt;
      var baseActor = baseEvent.EventActor;
      var typedActor = evt.EventActor;

      Assert.Same(baseActor, typedActor);
    }

    #endregion

    #region ContainerCreateEvent

    [Fact]
    public void ContainerCreateEvent_Constructor_SetsActionAndType()
    {
      var evt = new ContainerCreateEvent();

      Assert.Equal(EventAction.Create, evt.Action);
      Assert.Equal(EventType.Container, evt.Type);
    }

    [Fact]
    public void ContainerCreateEvent_Actor_PropertiesCanBeSet()
    {
      var actor = new ContainerCreateEvent.ContainerCreateActor
      {
        Image = "ubuntu:22.04",
        Name = "my-ubuntu"
      };

      Assert.Equal("ubuntu:22.04", actor.Image);
      Assert.Equal("my-ubuntu", actor.Name);
    }

    [Fact]
    public void ContainerCreateEvent_FullConstruction()
    {
      var actor = new ContainerCreateEvent.ContainerCreateActor
      {
        Image = "postgres:16",
        Name = "db"
      };

      var evt = new ContainerCreateEvent
      {
        Scope = EventScope.Local,
        Time = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
        EventActor = actor
      };

      Assert.Equal(EventAction.Create, evt.Action);
      Assert.Equal(EventType.Container, evt.Type);
      Assert.Equal(EventScope.Local, evt.Scope);
      Assert.Equal("postgres:16", evt.EventActor.Image);
      Assert.Equal("db", evt.EventActor.Name);
    }

    #endregion

    #region ContainerStartEvent

    [Fact]
    public void ContainerStartEvent_Constructor_SetsActionAndType()
    {
      var evt = new ContainerStartEvent();

      Assert.Equal(EventAction.Start, evt.Action);
      Assert.Equal(EventType.Container, evt.Type);
    }

    [Fact]
    public void ContainerStartEvent_Actor_PropertiesCanBeSet()
    {
      var actor = new ContainerStartEvent.ContainerStartActor
      {
        Image = "node:20-alpine",
        Name = "api-server"
      };

      var evt = new ContainerStartEvent { EventActor = actor };

      Assert.Equal("node:20-alpine", evt.EventActor.Image);
      Assert.Equal("api-server", evt.EventActor.Name);
    }

    #endregion

    #region ContainerStopEvent

    [Fact]
    public void ContainerStopEvent_Constructor_SetsActionAndType()
    {
      var evt = new ContainerStopEvent();

      Assert.Equal(EventAction.Stop, evt.Action);
      Assert.Equal(EventType.Container, evt.Type);
    }

    [Fact]
    public void ContainerStopEvent_Actor_PropertiesCanBeSet()
    {
      var actor = new ContainerStopEvent.ContainerStopActor
      {
        Image = "mysql:8.0",
        Name = "database"
      };

      var evt = new ContainerStopEvent { EventActor = actor };

      Assert.Equal("mysql:8.0", evt.EventActor.Image);
      Assert.Equal("database", evt.EventActor.Name);
    }

    #endregion

    #region ContainerDieEvent

    [Fact]
    public void ContainerDieEvent_Constructor_SetsActionAndType()
    {
      var evt = new ContainerDieEvent();

      Assert.Equal(EventAction.Die, evt.Action);
      Assert.Equal(EventType.Container, evt.Type);
    }

    [Fact]
    public void ContainerDieEvent_Actor_HasExitCode()
    {
      var actor = new ContainerDieEvent.ContainerDieActor
      {
        Image = "alpine:latest",
        Name = "worker",
        ExitCode = "0"
      };

      var evt = new ContainerDieEvent { EventActor = actor };

      Assert.Equal("alpine:latest", evt.EventActor.Image);
      Assert.Equal("worker", evt.EventActor.Name);
      Assert.Equal("0", evt.EventActor.ExitCode);
    }

    [Fact]
    public void ContainerDieEvent_Actor_NonZeroExitCode()
    {
      var actor = new ContainerDieEvent.ContainerDieActor
      {
        Image = "myapp:v1",
        Name = "failing-container",
        ExitCode = "137"
      };

      Assert.Equal("137", actor.ExitCode);
    }

    [Fact]
    public void ContainerDieEvent_Actor_ExitCodeDefaultsToNull()
    {
      var actor = new ContainerDieEvent.ContainerDieActor();
      Assert.Null(actor.ExitCode);
    }

    #endregion

    #region ContainerDestroyEvent

    [Fact]
    public void ContainerDestroyEvent_Constructor_SetsActionAndType()
    {
      var evt = new ContainerDestroyEvent();

      Assert.Equal(EventAction.Destroy, evt.Action);
      Assert.Equal(EventType.Container, evt.Type);
    }

    [Fact]
    public void ContainerDestroyEvent_Actor_PropertiesCanBeSet()
    {
      var actor = new ContainerDestroyEvent.ContainerDestroyActor
      {
        Image = "redis:7-alpine",
        Name = "cache-node"
      };

      var evt = new ContainerDestroyEvent { EventActor = actor };

      Assert.Equal("redis:7-alpine", evt.EventActor.Image);
      Assert.Equal("cache-node", evt.EventActor.Name);
    }

    #endregion

    #region ContainerKillEvent

    [Fact]
    public void ContainerKillEvent_Constructor_SetsActionAndType()
    {
      var evt = new ContainerKillEvent();

      Assert.Equal(EventAction.Kill, evt.Action);
      Assert.Equal(EventType.Container, evt.Type);
    }

    [Fact]
    public void ContainerKillEvent_Actor_HasSignal()
    {
      var actor = new ContainerKillEvent.ContainerKillActor
      {
        Image = "nginx:latest",
        Name = "web-server",
        Signal = "15"
      };

      var evt = new ContainerKillEvent { EventActor = actor };

      Assert.Equal("nginx:latest", evt.EventActor.Image);
      Assert.Equal("web-server", evt.EventActor.Name);
      Assert.Equal("15", evt.EventActor.Signal);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("15")]
    [InlineData("SIGTERM")]
    [InlineData("SIGKILL")]
    public void ContainerKillEvent_Actor_AcceptsVariousSignalFormats(string signal)
    {
      var actor = new ContainerKillEvent.ContainerKillActor { Signal = signal };
      Assert.Equal(signal, actor.Signal);
    }

    [Fact]
    public void ContainerKillEvent_Actor_SignalDefaultsToNull()
    {
      var actor = new ContainerKillEvent.ContainerKillActor();
      Assert.Null(actor.Signal);
    }

    #endregion
  }
}
