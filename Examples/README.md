# FluentDocker Examples

This folder contains example projects demonstrating various features of FluentDocker v3.

## Examples

### Simple
Basic container creation and management using the v3 fluent API.

```bash
cd Simple && dotnet run
```

### EventDriven
Demonstrates Docker event streaming with the v3 IStreamDriver interface.

```bash
cd EventDriven && dotnet run
```

### DockerInDockerLinux
Shows how to interact with Docker when running inside a Docker container.

```bash
cd DockerInDockerLinux && dotnet run
```

### ContainerStats (v3 Feature)
Demonstrates new v3 features:
- Container resource monitoring (CPU, memory, network, disk I/O)
- Static IPv4 assignment with custom networks
- Network creation with custom subnets

```bash
cd ContainerStats && dotnet run
```

### ComposeV2 (v3 Feature)
Demonstrates new v3 features:
- Docker Compose V2 (uses `docker compose` command)
- Directory copy to/from containers
- TemplateString path interpolation (`${TEMP}`, `${RND}`, `${E_*}`)

```bash
cd ComposeV2 && dotnet run
```

## Running All Examples

```bash
cd Examples
dotnet build
```

## Prerequisites

- .NET 10.0 SDK
- Docker Desktop or Docker Engine
- FluentDocker library (referenced via project)
