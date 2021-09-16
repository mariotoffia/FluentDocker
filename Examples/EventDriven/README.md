# EventDriven

This project will show how to subscribe to events on a certain docker _daemon_ and what they are for firing up a single container.

Example Run:

```bash
Number of hosts:1
unix:///var/run/docker.sock native Running
Spinning up a postgres and wait for ready state...
Service is running
Events:
Ductus.FluentDocker.Model.Events.ContainerCreateEvent
Ductus.FluentDocker.Model.Events.NetworkConnectEvent
Ductus.FluentDocker.Model.Events.ContainerStartEvent
```