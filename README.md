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
> CI run. See the [release-verification table](docs/testing/test-categories.md#release-verification)
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

For 3.2 previews, add `--prerelease` to the package commands.

## Quick Start

Start an nginx container and read its published endpoint. Every `using` below is
required to compile in a clean project — `ToHostExposedEndpointAsync` lives in
`FluentDocker.Services.Extensions`.

```csharp
using System;
using System.Linq;
using FluentDocker.Builders;
using FluentDocker.Kernel;
using FluentDocker.Services.Extensions;   // ToHostExposedEndpointAsync

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

var endpoint = await results.Containers.First().ToHostExposedEndpointAsync("80/tcp");
Console.WriteLine($"nginx is at {endpoint.Address}:{endpoint.Port}");
```

> A synchronous `Build()` wrapper exists, but it blocks on the async pipeline.
> Prefer `await using` + `BuildAsync()`, and avoid the sync wrapper inside ASP.NET,
> UI, or async test contexts where sync-over-async can deadlock.

## Start Here

New to FluentDocker? Start with the getting-started guide, then use the documentation
index to go deeper:

- [Getting Started](docs/getting-started.md) — first working container
- [Documentation index](docs/index.md) — guides by level and reading plans by role
- **[Documentation site](https://mariotoffia.github.io/FluentDocker/)** — built by the Pages
  workflow on pushes to `master`/`main` that touch `docs/**`, `FluentDocker/**`, or the
  workflow itself; 3.2 preview pages publish from [`docs/`](docs) once merged there.

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
| [Container / Image / Network / Volume](docs/containers.md) | yes | yes | yes |
| [System / Auth / Streaming](docs/advanced-drivers.md#streaming) | yes | yes | yes |
| [Compose](docs/compose.md) | yes | - | - |
| [Stack (Swarm)](docs/advanced-drivers.md#stack-swarm) | yes | - | - |
| [Service (Swarm)](docs/advanced-drivers.md#service-swarm) | yes | yes | - |
| [Pods](docs/advanced-drivers.md#pods) | - | - | yes |
| [Kubernetes play/generate](docs/podman.md) | - | - | yes |
| [Machine management](docs/advanced-drivers.md#machine-management) | - | - | yes |
| [Multi-arch manifests](docs/advanced-drivers.md#multi-arch-manifests) | - | - | yes |

Register multiple drivers in one kernel and switch scope with `WithinDriver` (or the
typed `WithinDockerCli` / `WithinDockerApi` / `WithinPodmanCli`). See
[Driver scopes](docs/getting-started.md#driver-scopes) for when to use each:

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

Maintained driver notes and guides cover [Compose](docs/compose.md),
[Docker API](docs/docker-api.md), [Podman](docs/podman.md), [Networking](docs/networking.md),
[Volumes](docs/volumes.md), [Model Runner](docs/model-runner.md), [Testing](docs/testing.md),
[Migration](docs/migration.md), [Troubleshooting](docs/troubleshooting.md),
[Architecture](docs/architecture.md), and [Utilities](docs/utilities.md).

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

See the [testing docs](docs/testing.md) for NUnit, MSTest, Compose, Swarm stack,
Podman Kubernetes, topology, and model resource types.

## Docker Model Runner — Local LLMs *(preview, 3.2.0-preview.2)*

> **Preview — not on NuGet yet.** Model Runner support lands in **3.2.0-preview.2**, which
> isn't published yet; build from the [preview branch](https://github.com/mariotoffia/FluentDocker/tree/featrure/model-support) to
> use it (the latest published package, 3.1.0, has no Model Runner). The inference DTO shapes
> may still change. Everything above is the stable surface — reach for this section only once
> you need local models.

FluentDocker manages and consumes **local LLMs** through Docker Model Runner — and any
OpenAI-compatible runner (vLLM, LM Studio, `llama-server`, hosted) — behind the same
`Builder → WithinDriver → UseXxx` pattern. The canonical quick start, context-size
guidance, and troubleshooting live in the model guide.

Full guide: **[Model Runner (local LLMs)](docs/model-runner.md)** ·
[runner plugins](docs/model-runner-plugins.md) · runnable [Examples/ModelRunner](https://github.com/mariotoffia/FluentDocker/tree/featrure/model-support/Examples/ModelRunner).

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

## Breaking changes (3.1.0 → 3.2.0)

3.2.0 tightens the core surface. If you consume the model/DTO types directly, check
these:

- **Nullable reference types.** The core `Model`, `Extensions`, and `Resources`
  namespaces (and almost all of `Common`) are now null-annotated (`#nullable
  enable`). Genuinely optional members are `T?`; the rest are non-null. Code
  compiled with nullable enabled may surface new warnings where it previously
  passed or ignored `null`.
- **Timestamps are `DateTimeOffset`.** `Container.Created`, `ContainerState.StartedAt`,
  `ContainerState.FinishedAt`, and `Volume.Created` changed from `DateTime` to
  `DateTimeOffset` — the engine's UTC offset is now preserved instead of discarded.
- **Enum values renumbered.** `RuntimeType` now starts at `Unknown = 0` and `DriverType`
  gained `Unknown = 0`. Numeric enum values are **not** a stable contract — serialize by
  name, never persist or transmit the number.
- **`ComposeServiceDefinition.Isolation`** is now `ContainerIsolationTechnology`
  (the old `ContainerIsolationType` enum was removed).
- **Removed types:** `ContainerIsolationType`, `ContainerSpecificConfig`, `ProcessRow`,
  `Processes`, and `PreferredDriverType` (all unused or superseded).
- **Moved:** `CapabilityChecks`, the `KernelCapabilityExtensions` extension methods
  (including `kernel.EnsureCapabilityAsync(...)`), and the `DriverCapability` enum moved
  from `FluentDocker.Common` to `FluentDocker.Kernel`. Update `using FluentDocker.Common;`
  to `using FluentDocker.Kernel;` — the extension-method move is otherwise a silent build break.
- **Obsolete:** `ContainerBuildParams` is now `[Obsolete]` (test-only; slated for removal).

For the complete list — including the builder/API and testing changes — see the
[CHANGELOG](CHANGELOG.md).

## Resources

- [Documentation Site](https://mariotoffia.github.io/FluentDocker/) — full docs on GitHub Pages
- [Migration Guide](docs/migration.md) — upgrading from v2.x
- [Architecture](docs/architecture.md) — v3 kernel/driver internals
- [Model Runner (local LLMs)](docs/model-runner.md) — managing & consuming local models
- [Changelog](CHANGELOG.md) — release notes
- [NuGet Package](https://www.nuget.org/packages/FluentDocker)

## Contributing

Contributions welcome! Please adhere to `.editorconfig` for code style.

## License

Apache 2.0 — see [LICENSE](LICENSE) for details.
