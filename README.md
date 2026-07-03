# FluentDocker

[![CI (build + unit)](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml/badge.svg)](https://github.com/mariotoffia/FluentDocker/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/mariotoffia/FluentDocker/branch/master/graph/badge.svg)](https://codecov.io/gh/mariotoffia/FluentDocker)
[![Release](https://img.shields.io/github/v/release/mariotoffia/FluentDocker?sort=semver&display_name=tag&color=brightgreen)](https://github.com/mariotoffia/FluentDocker/releases/latest)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](https://opensource.org/licenses/Apache-2.0)
[![.NET](https://img.shields.io/badge/.NET-net8.0%20%7C%20net10.0-blueviolet)](https://dotnet.microsoft.com/)

| Package | NuGet | Downloads |
|---------|:-----:|:---------:|
| FluentDocker | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.svg)](https://www.nuget.org/packages/FluentDocker) |
| Testing.Xunit | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.Testing.Xunit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.Xunit) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.Testing.Xunit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.Xunit) |
| Testing.MsTest | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.Testing.MsTest.svg)](https://www.nuget.org/packages/FluentDocker.Testing.MsTest) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.Testing.MsTest.svg)](https://www.nuget.org/packages/FluentDocker.Testing.MsTest) |
| Testing.NUnit | [![NuGet](https://img.shields.io/nuget/v/FluentDocker.Testing.NUnit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.NUnit) | [![Downloads](https://img.shields.io/nuget/dt/FluentDocker.Testing.NUnit.svg)](https://www.nuget.org/packages/FluentDocker.Testing.NUnit) |

> **CI badge scope:** the green CI badge proves **build + unit tests** across `net8.0`/`net10.0`.
> Docker, Podman, and Docker Model Runner integration suites run **on demand** (PR label,
> schedule, or manual dispatch) and when Docker is available — they are **not** part of every
> CI run. See the [release-verification table](docs/test-categories.md#release-verification)
> for what to run before shipping.

---

FluentDocker is a strong-named, **async-first** .NET library that drives Docker, Podman,
and (preview) Docker Model Runner behind one fluent `Builder → WithinDriver → UseXxx`
API. It targets `net8.0` and `net10.0` and is designed for development, testing, and
CI/CD.

## Install

```bash
dotnet add package FluentDocker
dotnet add package FluentDocker.Testing.Xunit   # xUnit adapter (optional)
dotnet add package FluentDocker.Testing.MsTest  # MSTest adapter (optional)
dotnet add package FluentDocker.Testing.NUnit   # NUnit adapter (optional)
```

## Quick Start

Start an nginx container and read its published endpoint. Every `using` below is
required to compile in a clean project — `ToHostExposedEndpoint` lives in
`FluentDocker.Services.Extensions`.

```csharp
using System;
using System.Linq;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;   // ToHostExposedEndpoint

// A kernel is the composition root; register one or more drivers. Multiple kernels
// per app are supported.
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

// await using + BuildAsync() is the first-class path: the whole graph is torn down
// (containers stopped and removed) when the results are disposed.
await using var results = await new Builder()
    .WithinDockerCli("docker", kernel)
    .UseContainer(c => c
        .UseImage("nginx:alpine")
        .ExposePort("80")
        .WaitForPort("80/tcp", 30000))
    .BuildAsync();

var endpoint = results.Containers.First().ToHostExposedEndpoint("80/tcp");
Console.WriteLine($"nginx is at {endpoint.Address}:{endpoint.Port}");
```

> A synchronous `Build()` wrapper exists, but it blocks on the async pipeline.
> Prefer `await using` + `BuildAsync()`, and avoid the sync wrapper inside ASP.NET,
> UI, or async test contexts where sync-over-async can deadlock.

## Start Here

New to FluentDocker? Follow the docs site — it is the single source of truth for the
full API and per-driver guides:

- **[Documentation site](https://mariotoffia.github.io/FluentDocker/)** — full docs
- [Learning Path](docs/learning-path.md) — beginner → advanced map
- [Getting Started](docs/getting-started.md) — first working container
- [Containers](docs/containers.md) · [Compose](docs/compose.md) · [Networking](docs/networking.md) · [Volumes](docs/volumes.md) · [Images](docs/images.md)
- [Docker API driver (production notes)](docs/docker-api.md) · [Podman production notes](docs/podman.md) · [Troubleshooting](docs/troubleshooting.md)
- [Testing](docs/testing.md) · [Architecture](docs/architecture.md) · [Migration v2 → v3](docs/migration.md)

## Drivers

FluentDocker ships three drivers, all resolved from the kernel by id:

- **Docker CLI** — shells out to the `docker` binary.
- **Docker API** — talks to the Docker Engine REST API over Unix socket, named pipe, or
  TCP+TLS; no CLI binary required. See [production notes](docs/docker-api.md).
- **Podman CLI** — shells out to `podman`; adds pods, machines, Kubernetes play, and
  multi-arch manifests. See [production notes](docs/podman.md).

All drivers share the core ports (`IContainerDriver`, `IImageDriver`, `INetworkDriver`,
`IVolumeDriver`, `ISystemDriver`, `IAuthDriver`, `IStreamDriver`) and add driver-specific
capabilities on top.

| Capability | Docker CLI | Docker API | Podman CLI |
|---|:---:|:---:|:---:|
| Container / Image / Network / Volume | yes | yes | yes |
| System / Auth / Streaming | yes | yes | yes |
| Compose | yes | - | - |
| Stack (Swarm) | yes | - | - |
| Service (Swarm) | yes | yes | - |
| Pods | - | - | yes |
| Kubernetes play/generate | - | - | yes |
| Machine management | - | - | yes |
| Multi-arch manifests | - | - | yes |

Register multiple drivers in one kernel and switch scope with `WithinDriver` (or the
typed `WithinDockerCli` / `WithinDockerApi` / `WithinPodmanCli`):

```csharp
using System;
using FluentDocker.Kernel;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d.AsDefault())
    .WithDockerApi("docker-api", d => d
        .WithConnectionTimeout(TimeSpan.FromSeconds(30)))
    .WithPodmanCli("podman", d => d.WithAutoStartMachine()) // macOS/Windows only
    .BuildAsync();
```

Per-driver walkthroughs (Compose, Swarm stack, Podman Kubernetes, pods, machines,
manifests, and direct `SysCtl<T>` access) live on the
[documentation site](https://mariotoffia.github.io/FluentDocker/).

## Test Support

`FluentDocker.Testing.Core` ships inside the main assembly; framework adapters are
separate packages (`FluentDocker.Testing.Xunit` targets **xUnit v3**).
Recommended entry points are `XunitContainerFixtureBase`,
`NUnitContainerFixtureBase`, and `MsTestContainerFixtureBase` (or
`MsTestClassContainerFixtureBase<T>` when a class-shared MSTest container is
needed).

```csharp
using FluentDocker.Builders;
using FluentDocker.Testing.Xunit;

// xUnit v3 — abstract base fixture (recommended):
public class MyRedisFixture : XunitContainerFixtureBase
{
  protected override void ConfigureContainer(IContainerBuilder builder)
    => builder
        .UseImage("redis:alpine")
        .ExposePort("6379")
        .WaitForPort("6379/tcp");
}
```

See the [testing docs](docs/testing.md) for NUnit, MSTest, Compose, Topology, Swarm
Stack, Podman Kubernetes, and model resource types.

## Docker Model Runner — Local LLMs *(preview, 3.2.0-preview.1)*

> **Preview.** Model Runner support is available since **3.2.0-preview.1**;
> the inference DTO shapes may still change. Everything above is the stable
> surface — reach for this section only once you need local models.

FluentDocker manages and consumes **local LLMs** through Docker Model Runner — and any
OpenAI-compatible runner (vLLM, LM Studio, `llama-server`, hosted) — behind the same
`Builder → WithinDriver → UseXxx` pattern. The canonical quick start, context-size
guidance, and troubleshooting live in the model guide.

Full guide: **[Model Runner (local LLMs)](docs/model-runner.md)** ·
[runner plugins](docs/model-runner-plugins.md) · runnable [Examples/ModelRunner](Examples/ModelRunner).

## Linux Users

Docker often needs `sudo`. Configure it per driver — the password (when used) is written
to `sudo`'s **stdin**, never placed on the command line:

```csharp
using FluentDocker.Kernel;
using FluentDocker.Model.Common;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerCli("docker", d => d
        .WithSudo(SudoMechanism.NoPassword)   // experimental enum; relies on NOPASSWD
        .AsDefault())
    .BuildAsync();
```

Or add your user to the docker group and skip sudo entirely:
`sudo usermod -aG docker $USER`.

## v3

**v3.0.0** was a major rewrite — multi-driver kernel, async-first API, Podman support,
new test packages. See the [3.0.0 release notes](https://github.com/mariotoffia/FluentDocker/releases/tag/3.0.0)
and the [migration guide](docs/migration.md) for the full feature list and breaking
changes.

## Resources

- [Documentation Site](https://mariotoffia.github.io/FluentDocker/) — full docs on GitHub Pages
- [Migration Guide](docs/migration.md) — upgrading from v2.x
- [Architecture](docs/architecture.md) — v3 kernel/driver internals
- [Model Runner (local LLMs)](docs/model-runner.md) — managing & consuming local models
- [NuGet Package](https://www.nuget.org/packages/FluentDocker)

## Contributing

Contributions welcome! Please adhere to `.editorconfig` for code style.

## License

Apache 2.0 — see [LICENSE](LICENSE) for details.
