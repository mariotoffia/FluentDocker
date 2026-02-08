# Podman CLI Driver Implementation Analysis

## Overview

This document analyzes Podman CLI commands relative to the FluentDocker driver architecture.
Phase 1 (Core Compatibility), Pod Management, and Kubernetes Integration have been implemented. This document tracks current state and future work.

---

## 1. Implemented Commands (Phase 1: Core Compatibility)

### 1.1 Container Driver (`IContainerDriver`)

Implementation: `PodmanCliContainerDriver.cs` + `PodmanCliContainerDriver.Operations.cs`

| Podman Command | Description | Docker Parity | Status |
|----------------|-------------|---------------|--------|
| `podman create` | Create container | `docker create` | ✅ Done |
| `podman run` | Run container | `docker run` | ✅ Done |
| `podman start` | Start container | `docker start` | ✅ Done |
| `podman stop` | Stop container | `docker stop` | ✅ Done |
| `podman restart` | Restart container | `docker restart` | ✅ Done |
| `podman pause` | Pause container | `docker pause` | ✅ Done |
| `podman unpause` | Unpause container | `docker unpause` | ✅ Done |
| `podman kill` | Kill container | `docker kill` | ✅ Done |
| `podman rm` | Remove container | `docker rm` | ✅ Done |
| `podman wait` | Wait for container | `docker wait` | ✅ Done |
| `podman ps` | List containers | `docker ps` | ✅ Done |
| `podman rename` | Rename container | `docker rename` | ✅ Done |
| `podman update` | Update container config | `docker update` | ✅ Done |
| `podman exec` | Execute in container | `docker exec` | ✅ Done |
| `podman top` | Display processes | `docker top` | ✅ Done |
| `podman logs` | Fetch logs | `docker logs` | ✅ Done |
| `podman stats` | Resource statistics | `docker stats` | ✅ Done |
| `podman cp` | Copy files | `docker cp` | ✅ Done |
| `podman diff` | Filesystem changes | `docker diff` | ✅ Done |
| `podman export` | Export filesystem | `docker export` | ✅ Done |
| `podman inspect` | Inspect container | `docker inspect` | ✅ Done |

**Not Implemented:** `podman init` (Podman-specific prep step, low priority)

---

### 1.2 Image Driver (`IImageDriver`)

Implementation: `PodmanCliImageDriver.cs`

| Podman Command | Description | Docker Parity | Status |
|----------------|-------------|---------------|--------|
| `podman pull` | Pull image | `docker pull` | ✅ Done |
| `podman push` | Push image | `docker push` | ✅ Done |
| `podman build` | Build image | `docker build` | ✅ Done |
| `podman images` | List images | `docker images` | ✅ Done |
| `podman rmi` | Remove image | `docker rmi` | ✅ Done |
| `podman tag` | Tag image | `docker tag` | ✅ Done |
| `podman history` | Image history | `docker history` | ✅ Done |
| `podman save` | Save to tar | `docker save` | ✅ Done |
| `podman load` | Load from tar | `docker load` | ✅ Done |
| `podman import` | Import tarball | `docker import` | ✅ Done |
| `podman image inspect` | Inspect image | `docker image inspect` | ✅ Done |
| `podman image prune` | Prune images | `docker image prune` | ✅ Done |

---

### 1.3 Network Driver (`INetworkDriver`)

Implementation: `PodmanCliNetworkDriver.cs`

| Podman Command | Description | Docker Parity | Status |
|----------------|-------------|---------------|--------|
| `podman network create` | Create network | `docker network create` | ✅ Done |
| `podman network rm` | Remove network | `docker network rm` | ✅ Done |
| `podman network ls` | List networks | `docker network ls` | ✅ Done |
| `podman network inspect` | Inspect network | `docker network inspect` | ✅ Done |
| `podman network connect` | Connect container | `docker network connect` | ✅ Done |
| `podman network disconnect` | Disconnect container | `docker network disconnect` | ✅ Done |
| `podman network prune` | Prune networks | `docker network prune` | ✅ Done |

---

### 1.4 Volume Driver (`IVolumeDriver`)

Implementation: `PodmanCliVolumeDriver.cs`

| Podman Command | Description | Docker Parity | Status |
|----------------|-------------|---------------|--------|
| `podman volume create` | Create volume | `docker volume create` | ✅ Done |
| `podman volume rm` | Remove volume | `docker volume rm` | ✅ Done |
| `podman volume ls` | List volumes | `docker volume ls` | ✅ Done |
| `podman volume inspect` | Inspect volume | `docker volume inspect` | ✅ Done |
| `podman volume prune` | Prune volumes | `docker volume prune` | ✅ Done |

---

### 1.5 System Driver (`ISystemDriver`)

Implementation: `PodmanCliSystemDriver.cs`

| Podman Command | Description | Docker Parity | Status |
|----------------|-------------|---------------|--------|
| `podman info` | System information | `docker info` | ✅ Done |
| `podman version` | Version info | `docker version` | ✅ Done |
| `podman system df` | Disk usage | `docker system df` | ✅ Done |
| `podman system prune` | Remove unused data | `docker system prune` | ✅ Done |

**Daemonless adaptations:** Ping uses `podman info`, no daemon switching, always Linux engine.

---

### 1.6 Auth Driver (`IAuthDriver`)

Implementation: `PodmanCliAuthDriver.cs`

| Podman Command | Description | Docker Parity | Status |
|----------------|-------------|---------------|--------|
| `podman login` | Login to registry | `docker login` | ✅ Done |
| `podman logout` | Logout from registry | `docker logout` | ✅ Done |

---

### 1.7 Stream Driver (`IStreamDriver`)

Implementation: `PodmanCliStreamDriver.cs`

| Podman Command | Description | Docker Parity | Status |
|----------------|-------------|---------------|--------|
| `podman logs --follow` | Stream logs | `docker logs -f` | ✅ Done |
| `podman events` | Stream events | `docker events` | ✅ Done |
| `podman stats` (streaming) | Stream stats | `docker stats` | ✅ Done |

**Note:** `AttachAsync` returns fail (not supported in CLI streaming mode).

---

### 1.8 Pod Driver (`IPodmanPodDriver`) — Podman-Specific

Implementation: `PodmanCliPodDriver.cs`

| Podman Command | Description | Status |
|----------------|-------------|--------|
| `podman pod create` | Create pod | ✅ Done |
| `podman pod start` | Start pod | ✅ Done |
| `podman pod stop` | Stop pod | ✅ Done |
| `podman pod restart` | Restart pod | ✅ Done |
| `podman pod kill` | Kill pod | ✅ Done |
| `podman pod pause` | Pause pod | ✅ Done |
| `podman pod unpause` | Unpause pod | ✅ Done |
| `podman pod rm` | Remove pod | ✅ Done |
| `podman pod ps` | List pods | ✅ Done |
| `podman pod inspect` | Inspect pod | ✅ Done |

**Not Implemented:** `podman pod logs/top/stats` (monitoring), `podman pod exists/clone` (utilities)

**Container integration:** `ContainerCreateConfig.Pod` + `--pod` flag wired in `BuildCreateArgs`.

---

### 1.9 Kubernetes Driver (`IPodmanKubernetesDriver`) — Podman-Specific

Implementation: `PodmanCliKubernetesDriver.cs`

| Podman Command | Description | Status |
|----------------|-------------|--------|
| `podman kube play` | Deploy K8s YAML | ✅ Done |
| `podman kube down` | Tear down from YAML | ✅ Done |
| `podman kube generate` | Generate K8s YAML | ✅ Done |

**Features:** `--network`, `--configmap`, `--log-driver`, `--replace`, `--start=false`, `--annotation` flags supported.

**Output parsing:** Handles both JSON format (newer Podman) and line-based Pod:/Container: format (older Podman).

### 1.10 Machine Driver (`IPodmanMachineDriver`) — Podman-Specific

Implementation: `PodmanCliMachineDriver.cs`

| Podman Command | Description | Status |
|----------------|-------------|--------|
| `podman machine init` | Initialize VM | ✅ Done |
| `podman machine start` | Start VM | ✅ Done |
| `podman machine stop` | Stop VM | ✅ Done |
| `podman machine rm` | Remove VM | ✅ Done |
| `podman machine list` | List machines | ✅ Done |
| `podman machine inspect` | Inspect machine | ✅ Done |
| `podman machine ssh` | SSH into machine | ✅ Done |
| `podman machine set` | Set machine config | ✅ Done |
| `podman machine info` | Host machine info | ✅ Done |

**Features:** `--cpus`, `--disk-size`, `--memory`, `--rootful`, `--image`, `--username`, `--now`, `-v` (volumes) flags supported for init. `--cpus`, `--disk-size`, `--memory`, `--rootful` for set.

**JSON parsing:** Handles `list --format json` (array), `inspect` (object or array), `info --format json` (nested Host/Version).

---

## 2. Future Work (Not Yet Implemented)

### 2.1 Manifest/Multi-Arch (Phase 2)

Would need: `IPodmanManifestDriver`

| Command | Description | Priority |
|---------|-------------|----------|
| `podman manifest create/add/push` | Multi-arch images | HIGH |
| `podman manifest annotate/inspect/rm` | Manifest management | MEDIUM |

### 2.3 Not Planned

These commands are not planned for implementation:

- **Healthcheck** (`podman healthcheck run`): Embedded in container lifecycle, manual run is edge case
- **Advanced** (`podman mount/unmount/unshare/auto-update/farm/artifact/quadlet`): Niche/Linux-specific
- **Swarm** (`docker service/stack/swarm/node`): Podman does not support Docker Swarm
- **Secret management** (`podman secret`): Low adoption, consider on demand
- **Generation** (`podman generate systemd/spec`): Linux-specific, consider on demand
- **Compose**: Podman uses third-party `podman-compose`; prefer `podman play kube`

---

## 3. Architecture

### 3.1 File Structure

```
FluentDocker/Drivers/Podman/Cli/
├── Binary/
│   ├── PodmanBinaryType.cs
│   ├── PodmanBinary.cs
│   ├── IPodmanBinaryResolver.cs
│   ├── PodmanBinariesResolver.cs
│   └── PodmanBinaryConfiguration.cs
├── Components/
│   ├── PodmanCliAuthDriver.cs
│   ├── PodmanCliContainerDriver.cs
│   ├── PodmanCliContainerDriver.Operations.cs
│   ├── PodmanCliImageDriver.cs
│   ├── PodmanCliNetworkDriver.cs
│   ├── PodmanCliStreamDriver.cs
│   ├── PodmanCliSystemDriver.cs
│   ├── PodmanCliVolumeDriver.cs
│   ├── PodmanCliPodDriver.cs
│   ├── PodmanCliKubernetesDriver.cs
│   └── PodmanCliMachineDriver.cs
├── PodmanCliDriverBase.cs
└── PodmanCliDriverPack.cs
```

### 3.2 Key Differences from Docker

**Daemonless Architecture:**
```
Docker:  FluentDocker → Docker CLI → Docker Daemon → Container Runtime
Podman:  FluentDocker → Podman CLI → Container Runtime (direct)
```

**Rootless Operation:**
- Podman is rootless by default
- Storage: `~/.local/share/containers/storage`
- Auth: `~/.config/containers/auth.json`

**Binary Resolution:**
- `PodmanClient` (podman/podman.exe)
- `PodmanRemote` (podman-remote/podman-remote.exe)

---

## 4. Capabilities

| Feature | Docker Driver | Podman Driver |
|---------|---------------|---------------|
| Containers | ✅ Full | ✅ Full |
| Images | ✅ Full | ✅ Full |
| Networks | ✅ Full | ✅ Full (CNI/netavark) |
| Volumes | ✅ Full | ✅ Full |
| System | ✅ Full | ✅ Adapted (daemonless) |
| Auth | ✅ Full | ✅ Full |
| Streaming | ✅ Full | ✅ Full (no attach) |
| Pods | ❌ No | ✅ Full (lifecycle + inspect) |
| Kubernetes | ❌ No | ✅ Full (play + down + generate) |
| Machines | ❌ No | ✅ Full (init + lifecycle + ssh + info) |
| Swarm | ✅ Yes | ❌ N/A |
| Compose | ✅ Native V2 | ❌ Skipped |
| Rootless | ⚠️ Experimental | ✅ Default |
| Daemonless | ❌ No | ✅ Yes |

---

## 5. References

- [Podman Commands Reference](https://docs.podman.io/en/stable/Commands.html)
- [Podman vs Docker Comparison](https://last9.io/blog/podman-vs-docker/)
- Docker CLI Driver: `/FluentDocker/Drivers/Docker/Cli/`
- Driver Interfaces: `IContainerDriver`, `IImageDriver`, etc.

---

**Document Version:** 3.0
**Last Updated:** 2026-02-08
**Status:** Phase 1 Complete + Pod Driver + Kubernetes Integration + Machine Management, Phase 2 Planned
