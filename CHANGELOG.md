# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.0.0] - 2026-05-11

### Added

- **Multi-driver kernel architecture** — `FluentDockerKernel` manages multiple driver packs (`IDriverPack`) via `IDriverRegistry` with async lifecycle
- **Docker Engine API driver** — full REST API driver communicating over Unix socket, named pipe, or TCP+TLS; 8 component drivers (Container, Image, Network, Volume, System, Auth, Stream, Service) with automatic API version negotiation
- **Podman CLI driver** — complete Podman CLI integration with binary resolution, container/image/network/volume/pod/manifest operations, and machine management
- **Fluent builder system** — `Builder` with `WithinDriver()` entry point and lambda-based sub-builders for containers, networks, volumes, compose, images, and pods
- **Wait conditions** — port, HTTP, process, log, health check, and custom lambda wait conditions with configurable timeouts and poll intervals
- **Testing framework** — `FluentDocker.Testing.Xunit`, `FluentDocker.Testing.NUnit`, `FluentDocker.Testing.MsTest` packages with resource lifecycle management (`ContainerResource`, `ComposeResource`, `TopologyResource`, `ImageResource`, `NetworkResource`, `VolumeResource`)
- **Orphan cleanup** — label-based session tracking (`fluentdocker.session`) with `OrphanCleanup` utility for sweeping leaked containers
- **Security builder methods** — `WithCapAdd`, `WithCapDrop`, `WithSecurityOpt`, `WithReadonlyRootfs`, `WithShmSize`, `WithTmpfs`, `WithDevice`, `WithPlatform`, `WithRuntime`
- **Builder validation** — `Validate()` at build time catches missing images, invalid port mappings, and conflicting options
- **Volume model expansion** — `Mountpoint`, `Labels`, `Options`, `UsageData` properties
- **XML documentation file** — NuGet package now includes IntelliSense XML docs
- **CI/CD** — GitHub Actions with OS matrix, scheduled integration tests, pack validation

### Changed

- **Async-first API** — all driver and service operations are async with `CancellationToken` support
- **`IDriverPack` extends `IDriverInterfaceResolver`** — eliminates cast patterns; packs directly support `TryResolve` and `GetSupportedInterfaces`
- **Central package management** — `Directory.Packages.props` for dependency version control
- **Nullable annotations** — enabled across all projects
- **.NET 8 + .NET 10** multi-targeting

### Deprecated

- `IService` (sync) — use `IServiceAsync` instead; sync methods wrap async with `.GetAwaiter().GetResult()` which can deadlock
- `FluentDocker.Model.Containers.CommandResponse<T>` — use `FluentDocker.Model.Drivers.CommandResponse<T>` instead
- `FluentDocker.Services.NetworkCreateConfig` — use `FluentDocker.Drivers.NetworkCreateConfig` instead
- `IFeature`, `FeatureAttribute`, `FeatureConstants` — v2 legacy types, will be removed in v4

### Removed

- Legacy `Fd` static helper class
- Old Docker Machine command argument structures
- `FluentDocker.XUnit` and `FluentDocker.MsTest` packages (replaced by `FluentDocker.Testing.*`)
- `DriverComponent` enum and `ISysCtl.SysCtl(string, DriverComponent)` overload — use generic `SysCtl<T>(driverId)` or `SysCtl(driverId, Type)` instead

### Fixed

- **Process resource leaks** — `Process` objects now properly disposed via `using` in CLI driver bases
- **Process orphaning on cancellation** — child processes killed on `CancellationToken` cancellation
- **API version negotiation race** — thread-safe one-time negotiation with `SemaphoreSlim`
- **sudo password exposure** — password no longer passed as CLI argument; uses stdin redirection
- **Registry password on CLI** — `--password-stdin` is now the default
- **`DriverRegistry` TOCTOU race** — registration uses lock around check-initialize-add sequence
- **HTTP wait timeout reset** — uses remaining time instead of full timeout per iteration
- **Docker stream header parsing** — operates on raw bytes to handle multi-byte UTF-8 correctly
- **Docker CLI logs** — now includes stderr output (Docker writes logs to stderr by default)
- **Entrypoint quoting** — only passes the executable as `--entrypoint`; args go to `Cmd`
- **Env var quoting** — values with spaces or metacharacters are now properly quoted
- **Stream disposal** — `using` on API stream connections prevents leaks on early cancellation
- **`FluentDockerKernel.Dispose` deadlock** — uses `Task.Run` to avoid sync-context deadlock
- **Build warnings** — eliminated 995 build warnings across all projects (zero-warning build)
- Process output reading: replaced event-based `BeginOutputReadLine` with `ReadToEndAsync` to fix async flush race conditions
- Per-call `HttpClient` creation replaced with shared instance in Docker API driver
- `Stopwatch` used for timing instead of `DateTime.UtcNow` subtraction
- CLI argument quoting for values containing spaces and shell metacharacters
- `ContinueWith` usage replaced with proper `await` patterns

## [2.x] - Previous

See [GitHub releases](https://github.com/mariotoffia/FluentDocker/releases) for v2.x history.
