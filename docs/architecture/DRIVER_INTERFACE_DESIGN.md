# Driver Interface Design - Complete Feature Matrix

This document provides the complete interface design for the FluentDocker driver layer, ensuring feature parity with the Commands namespace and extensibility for Podman and Kubernetes.

## Interface Summary

| Interface | Docker | Podman | Kubernetes | Purpose |
|-----------|--------|--------|------------|---------|
| IDriver | ✅ | ✅ | ✅ | Base interface |
| IContainerDriver | ✅ | ✅ | ⚠️ Pods | Container lifecycle |
| IImageDriver | ✅ | ✅ | ❌ | Image management |
| INetworkDriver | ✅ | ✅ | ⚠️ | Network management |
| IVolumeDriver | ✅ | ✅ | ⚠️ PVC | Volume management |
| IComposeDriver | ✅ | ⚠️ | ❌ | Compose operations |
| ISystemDriver | ✅ | ✅ | ❌ | System info |
| **IAuthDriver** | ✅ | ✅ | ✅ | Registry auth |
| **IStreamDriver** | ✅ | ✅ | ⚠️ | Streaming ops |
| **IStackDriver** | ✅ Swarm | ❌ | ✅ | Orchestration |
| **IServiceDriver** | ✅ Swarm | ❌ | ✅ | Service management |
| **IMachineDriver** | ✅ | ❌ | ❌ | Docker Machine |

**Legend:** ✅ Full Support | ⚠️ Partial/Different API | ❌ Not Applicable

---

## Complete Command Mapping

### IContainerDriver Methods

| Method | Docker Command | Podman | K8s |
|--------|---------------|--------|-----|
| CreateAsync | `docker create` | ✅ | ⚠️ |
| StartAsync | `docker start` | ✅ | ⚠️ |
| StopAsync | `docker stop` | ✅ | ⚠️ |
| RemoveAsync | `docker rm` | ✅ | ⚠️ |
| InspectAsync | `docker inspect` | ✅ | ⚠️ |
| ListAsync | `docker ps` | ✅ | ⚠️ |
| GetLogsAsync | `docker logs` | ✅ | ⚠️ |
| **RunAsync** | `docker run` | ✅ | ⚠️ |
| **PauseAsync** | `docker pause` | ✅ | ❌ |
| **UnpauseAsync** | `docker unpause` | ✅ | ❌ |
| **KillAsync** | `docker kill` | ✅ | ⚠️ |
| **RestartAsync** | `docker restart` | ✅ | ⚠️ |
| **ExecAsync** | `docker exec` | ✅ | ⚠️ |
| **CopyToAsync** | `docker cp` (to) | ✅ | ⚠️ |
| **CopyFromAsync** | `docker cp` (from) | ✅ | ⚠️ |
| **ExportAsync** | `docker export` | ✅ | ❌ |
| **DiffAsync** | `docker diff` | ✅ | ❌ |
| **TopAsync** | `docker top` | ✅ | ❌ |
| **RenameAsync** | `docker rename` | ✅ | ❌ |
| **WaitAsync** | `docker wait` | ✅ | ⚠️ |
| **UpdateAsync** | `docker update` | ✅ | ⚠️ |

### IImageDriver Methods

| Method | Docker Command | Podman | K8s |
|--------|---------------|--------|-----|
| PullAsync | `docker pull` | ✅ | ❌ |
| RemoveAsync | `docker rmi` | ✅ | ❌ |
| BuildAsync | `docker build` | ✅ | ❌ |
| ListAsync | `docker images` | ✅ | ❌ |
| InspectAsync | `docker image inspect` | ✅ | ❌ |
| TagAsync | `docker tag` | ✅ | ❌ |
| **PushAsync** | `docker push` | ✅ | ❌ |
| **HistoryAsync** | `docker history` | ✅ | ❌ |
| **SaveAsync** | `docker save` | ✅ | ❌ |
| **LoadAsync** | `docker load` | ✅ | ❌ |
| **ImportAsync** | `docker import` | ✅ | ❌ |
| **PruneAsync** | `docker image prune` | ✅ | ❌ |

### INetworkDriver Methods

| Method | Docker Command | Podman | K8s |
|--------|---------------|--------|-----|
| CreateAsync | `docker network create` | ✅ | ⚠️ |
| RemoveAsync | `docker network rm` | ✅ | ⚠️ |
| ListAsync | `docker network ls` | ✅ | ⚠️ |
| ConnectAsync | `docker network connect` | ✅ | ❌ |
| DisconnectAsync | `docker network disconnect` | ✅ | ❌ |
| InspectAsync | `docker network inspect` | ✅ | ⚠️ |
| PruneAsync | `docker network prune` | ✅ | ❌ |

### IVolumeDriver Methods

| Method | Docker Command | Podman | K8s |
|--------|---------------|--------|-----|
| CreateAsync | `docker volume create` | ✅ | ⚠️ PVC |
| RemoveAsync | `docker volume rm` | ✅ | ⚠️ |
| ListAsync | `docker volume ls` | ✅ | ⚠️ |
| InspectAsync | `docker volume inspect` | ✅ | ⚠️ |
| PruneAsync | `docker volume prune` | ✅ | ❌ |

### IComposeDriver Methods

| Method | Docker Command | Podman |
|--------|---------------|--------|
| UpAsync | `docker compose up` | ⚠️ |
| DownAsync | `docker compose down` | ⚠️ |
| StartAsync | `docker compose start` | ⚠️ |
| StopAsync | `docker compose stop` | ⚠️ |
| ListAsync | `docker compose ps` | ⚠️ |
| GetLogsAsync | `docker compose logs` | ⚠️ |
| ExecuteAsync | `docker compose exec` | ⚠️ |
| **BuildAsync** | `docker compose build` | ⚠️ |
| **PullAsync** | `docker compose pull` | ⚠️ |
| **PauseAsync** | `docker compose pause` | ⚠️ |
| **UnpauseAsync** | `docker compose unpause` | ⚠️ |
| **RestartAsync** | `docker compose restart` | ⚠️ |
| **KillAsync** | `docker compose kill` | ⚠️ |
| **RunAsync** | `docker compose run` | ⚠️ |
| **ConfigAsync** | `docker compose config` | ⚠️ |
| **TopAsync** | `docker compose top` | ⚠️ |
| **ImagesAsync** | `docker compose images` | ⚠️ |
| **CopyAsync** | `docker compose cp` | ⚠️ |
| **ScaleAsync** | `docker compose scale` | ⚠️ |

### ISystemDriver Methods

| Method | Docker Command | Podman | K8s |
|--------|---------------|--------|-----|
| GetInfoAsync | `docker info` | ✅ | ❌ |
| GetVersionAsync | `docker version` | ✅ | ❌ |
| PingAsync | `docker system ping` | ✅ | ❌ |
| **GetDiskUsageAsync** | `docker system df` | ✅ | ❌ |
| **PruneAsync** | `docker system prune` | ✅ | ❌ |
| **IsWindowsEngineAsync** | Check OS type | ❌ | ❌ |

### IAuthDriver Methods (NEW)

| Method | Docker Command | Podman | K8s |
|--------|---------------|--------|-----|
| LoginAsync | `docker login` | ✅ | ⚠️ |
| LogoutAsync | `docker logout` | ✅ | ⚠️ |

### IStreamDriver Methods (NEW)

| Method | Docker Command | Podman | K8s |
|--------|---------------|--------|-----|
| StreamLogsAsync | `docker logs -f` | ✅ | ⚠️ |
| StreamEventsAsync | `docker events` | ✅ | ⚠️ |
| StreamStatsAsync | `docker stats` | ✅ | ⚠️ |
| AttachAsync | `docker attach` | ✅ | ⚠️ |

### IStackDriver Methods (NEW)

| Method | Docker Command | K8s |
|--------|---------------|-----|
| ListAsync | `docker stack ls` | ⚠️ |
| GetTasksAsync | `docker stack ps` | ⚠️ |
| DeployAsync | `docker stack deploy` | ⚠️ |
| RemoveAsync | `docker stack rm` | ⚠️ |
| GetServicesAsync | `docker stack services` | ⚠️ |

### IServiceDriver Methods (NEW)

| Method | Docker Command | K8s |
|--------|---------------|-----|
| CreateAsync | `docker service create` | ⚠️ |
| ListAsync | `docker service ls` | ⚠️ |
| RemoveAsync | `docker service rm` | ⚠️ |
| InspectAsync | `docker service inspect` | ⚠️ |
| GetTasksAsync | `docker service ps` | ⚠️ |
| ScaleAsync | `docker service scale` | ⚠️ |
| UpdateAsync | `docker service update` | ⚠️ |
| GetLogsAsync | `docker service logs` | ⚠️ |
| RollbackAsync | `docker service rollback` | ⚠️ |

### IMachineDriver Methods (NEW)

| Method | Docker Command |
|--------|---------------|
| ListAsync | `docker-machine ls` |
| InspectAsync | `docker-machine inspect` |
| StartAsync | `docker-machine start` |
| StopAsync | `docker-machine stop` |
| CreateAsync | `docker-machine create` |
| DeleteAsync | `docker-machine rm` |
| GetEnvAsync | `docker-machine env` |
| GetUrlAsync | `docker-machine url` |
| GetStatusAsync | `docker-machine status` |

---

## Implementation Files

| Interface | File Location |
|-----------|--------------|
| IDriver | `Drivers/IDriver.cs` |
| IContainerDriver | `Drivers/IContainerDriver.cs` |
| IImageDriver | `Drivers/IImageDriver.cs` |
| INetworkDriver | `Drivers/INetworkDriver.cs` |
| IVolumeDriver | `Drivers/IVolumeDriver.cs` |
| IComposeDriver | `Drivers/IComposeDriver.cs` |
| ISystemDriver | `Drivers/ISystemDriver.cs` |
| IAuthDriver | `Drivers/IAuthDriver.cs` |
| IStreamDriver | `Drivers/IStreamDriver.cs` |
| IStackDriver | `Drivers/IStackDriver.cs` |
| IServiceDriver | `Drivers/IServiceDriver.cs` |
| IMachineDriver | `Drivers/IMachineDriver.cs` |
| DockerCliDriver | `Drivers/Docker/Cli/DockerCliDriver.cs` |

