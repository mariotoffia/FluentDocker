# AGENTS.md — FluentDocker (.NET)

Strong-named, async-first C# library (`net8.0;net10.0`, v3.2.0-preview.1) that drives Docker / Podman / Docker-Model-Runner through a fluent API. Clean + Hexagonal: a Ports-and-Adapters driver subsystem resolved at runtime through a kernel. Value objects exist (`ModelReference`, `ModelRunnerEndpoint` — immutable, `IEquatable`, validated); aggregates are minimal — Services orchestrate commands over driver ports. The Makefile is the command surface — analyse, build, and verify through it. **Follow YAGNI; prefer one-liners.**

## Principles
Follow YAGNI principles, and one-liner solutions.

## Layer map (`Model` is the innermost core; adapters implement port abstractions)
`Builders → Services → Drivers (ports = interfaces) ← adapters (impls);  all → Model`
- **Builders** `FluentDocker/Builders/` — fluent config; deferred `BuildAsync()` runs queued ops.
- **Kernel** `FluentDocker/Kernel/` — `FluentDockerKernel` (composition root, `ISysCtl`) + `DriverRegistry` (lifecycle). Non-singleton; create via `FluentDockerKernel.Create()...BuildAsync()`.
- **Drivers** `FluentDocker/Drivers/` — ports `IXxxDriver`; adapters under `Drivers/{Docker/Api,Docker/Cli,Podman/Cli}/Components/`.
- **Services** `FluentDocker/Services/` + `Services/Impl/` — `IServiceAsync` impls; resolve ports via `kernel.SysCtl<IXxxDriver>(driverId)`.
- **Model** `FluentDocker/Model/` — DTOs, `CommandResponse<T>`, enums, value objects. Legacy exceptions exist: builder configs hold service callbacks, compose configs hold service delegates, build/driver scopes carry logging abstractions, and some model builders import Common/Extensions. Do not move them in v3 (public API); keep new model code dependency-light.

## Analyse → Design → Architect → Implement → Test
- **Analyse**: read this file + `README.md`; trace one op end-to-end (`Builder.UseContainer` → `BuildAsync` → `SysCtl<IContainerDriver>` → adapter). Run `make build` first.
- **Design**: choose the smallest layer that owns the change; new behaviour starts as a **port interface**, never as a fat service or builder. Reuse `CommandResponse<T>` + existing enums.
- **Architect**: keep dependencies pointing at `Model` + port abstractions (dependency inversion). One port per capability; a `DriverPack` groups a runtime's adapters. No new kernel code to add a capability — `IDriverInterfaceResolver` handles it.
- **Implement**: follow the recipe below; match surrounding idiom.
- **Test**: public-API tests only (strong-named, no `InternalsVisibleTo`); `make check` before claiming done.

## Hard rules
- Code files **≤ 500 lines**, docs **≤ 600** — `wc -l` before editing; split with `partial class` / partial interface (e.g. `*.Operations.cs`).
- Small, segregated interfaces — one port per domain; split `.Operations` partials to stay under 500.
- **Async-first**: `IServiceAsync` is the contract; methods take `CancellationToken cancellationToken = default` and use `ConfigureAwait(false)`.
- New JSON → `Common/JsonHelper.cs` (`TryDeserialize<T>` returns `default`; check `JsonElement.ValueKind == Number` before `TryGetInt32`). Don't add Newtonsoft to new code.
- All CLI args → `QuoteArgumentIfNeeded` (in `Drivers/Docker/Cli/DockerCliDriverBase.cs` / `PodmanCliDriverBase.cs`).
- Use `CommandResponse<T>` from `FluentDocker.Model.Drivers`.
- `ImplicitUsings=disable` → explicit `using`s; 2-space indent, `var`, braces on new lines (`.editorconfig`).
- Strong-named, **no `InternalsVisibleTo`** → test through public surfaces, not internals.
- Dispose idempotent (`Interlocked.CompareExchange`); prefer `DisposeAsync()` — sync `Dispose()` is the fallback.
- Scratch / test output goes under `.out/` (git-ignored). Don't commit binaries.

## Resolution model
- `kernel.SysCtl<T>(driverId)` resolves a port: pack `TryResolve(type)` → driver `IDriverInterfaceResolver` → cast.
- `InterfaceNotSupportedException` = soft (`TrySysCtl` returns false); `DriverNotFoundException` = hard.
- Builder extensions resolve ports via `RequireDriver<T>()` (throws) / `TryDriver<T>()` (null) on `IDriverScopedBuilder` (exposes `Kernel` + `DriverId`).

## Add-a-feature recipe (port → adapter → service → builder)
1. **Port** `Drivers/IXxxDriver.cs` — segregated, `Task<CommandResponse<T>>` returns.
2. **Adapter(s)** `Drivers/<Runtime>/<Transport>/Components/<Runtime><Transport>XxxDriver.cs` — inherit `DockerCliDriverBase` / `DockerApiDriverBase`; quote args, parse with `JsonHelper`.
3. **Register** in the pack via `RegisterDriver<IXxxDriver>(impl)` (→ `Drivers[typeof(IXxxDriver)]`) — no kernel edits.
4. **Service** `Services/Impl/XxxService.cs` — `IServiceAsync` (+ `IServiceCapabilities`); resolve via `SysCtl`, map `CommandResponse` errors to exceptions.
5. **Builder** — method on `IXxxBuilder`, sealed internal builder implementing `IDriverScopedBuilder`, validate at execute time, wire into `Builder.UseXxx`. Model builders (`UseModelRunner`/`UseModel`) return directly and are **off** the deferred pipeline and **off** `IBuilder` — don't "fix" that. **Why two ways:** `UseContainer/Network/Volume/Image/Compose` build a *graph of resources* you create and tear down together, so they queue ops and `BuildAsync()` returns one `BuildResults` bag. `UseModelRunner` returns an `IModelRunner` *client* to an already-running runner (not an `IServiceAsync`, can't live in the bag); `UseModel` returns a typed `IModelService` handle directly so the one-model case stays one object, not a downcast from a collection. Direct return = right semantics, not an inconsistency.

## Verify (all via Makefile)
- `make check` (= `lint` + `test` + `test-runners` + `coverage-check`) before done; coverage floor is line 78 / branch 71 (measured on `Category=Unit`).
- `make test` — unit, net10.0 (`Category=Unit`); `make test-net8` for net8.0 (CI runs both).
- `make lint` — `dotnet format whitespace|style --verify-no-changes`; `make format` to fix.
- `make test-integration` (`Category=Integration`, needs Docker/Podman); `make test-dmr` (`Category=Integration&Requires=Dmr`, `FLUENTDOCKER_REQUIRE_DMR=1`).
- `make benchmark` (BenchmarkDotNet) for perf-sensitive changes.
- Tag every test `[Trait("Category","Unit|Integration|PodmanIntegration|DevLocal")]` (+ `[Trait("Requires",...)]`). Mocks in `FluentDocker.Tests/Mocks/` — `MockDriverPack` (partial, Moq), `MockModelApiConnection` (hand-rolled). xUnit v3 + Moq.
