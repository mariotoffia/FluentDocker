# EventDriven

This project will show how to subscribe to events on a certain docker _daemon_ and what they are for firing up a single container.

Example Run:

```bash
Number of hosts:1
unix:///var/run/docker.sock native Running
Spinning up a postgres and wait for ready state...
Service is running
Events:
FluentDocker.Model.Events.ContainerCreateEvent
FluentDocker.Model.Events.NetworkConnectEvent
FluentDocker.Model.Events.ContainerStartEvent
```