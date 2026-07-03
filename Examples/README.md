# FluentDocker Examples

This folder contains example projects demonstrating various features of FluentDocker v3.
Run commands from the repository root.

## Examples

### Simple
Basic container creation and management using the v3 fluent API.

```bash
dotnet run --project Examples/Simple -f net10.0
```

### EventDriven
Demonstrates Docker event streaming with the v3 IStreamDriver interface.

```bash
dotnet run --project Examples/EventDriven -f net10.0
```

### DockerInDockerLinux
Shows how to interact with Docker when running inside a Docker container.

```bash
dotnet run --project Examples/DockerInDockerLinux -f net10.0
```

### ContainerStats (v3 Feature)
Demonstrates new v3 features:
- Container resource monitoring (CPU, memory, network, disk I/O)
- Static IPv4 assignment with custom networks
- Network creation with custom subnets

```bash
dotnet run --project Examples/ContainerStats -f net10.0
```

### ComposeV2 (v3 Feature)
Demonstrates new v3 features:
- Docker Compose V2 (uses `docker compose` command)
- Directory copy to/from containers
- TemplateString path interpolation (`${TEMP}`, `${RND}`, `${E_*}`)

```bash
dotnet run --project Examples/ComposeV2 -f net10.0
```

### ModelRunner (v3.2 Feature)
Demonstrates Docker Model Runner (local LLMs):
- Pull/list models, one-shot and **streaming** chat, embeddings
- A model as a managed service (`UseModel`) that loads on start, unloads on dispose

Requires Docker Model Runner enabled (Docker Desktop → Settings → AI), with host-side TCP turned on for inference. Uses tiny models.

```bash
dotnet run --project Examples/ModelRunner -f net10.0
# -f net8.0 also works
```

## Running All Examples

```bash
dotnet build Examples --nologo
```

## Prerequisites

- .NET 10.0 SDK
- Docker Desktop or Docker Engine
- FluentDocker library (referenced via project)
