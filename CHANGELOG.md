# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.2.0-preview.2] - 2026-07-08

### Breaking

Production-readiness remediation of the preview API surface. Recompile and review call sites:

- **Dropped `net8.0`; the library now single-targets `net10.0`.** `<TargetFramework>` is `net10.0` only — net8.0 (and every earlier TFM) is no longer built or published. A net8.0 project that references this release fails to restore with a bare `NU1202` (the package is incompatible with `net8.0`). Retarget consumers to `net10.0` before upgrading.
- **`IContainerBuilder.WithPort(hostPort, containerPort)`** — parameter order flipped from `(containerPort, hostPort)` to match docker `-p host:container` and every other port API in the library. **Bare-number call sites compiled against 3.0/3.1 recompile cleanly with swapped semantics — review every `WithPort` call when upgrading.** `ExposePort` and `IPodBuilder.WithPort` already used host-first order and are unchanged.
- **`WaitForHttp(url, timeoutMs)` renamed `WaitForHttpUrl`** — the old name overload-shadowed the port-form `WaitForHttp("8080/tcp", 5000)`, which now binds the normalizing port overload as intended.
- **`WithVolume` / `ContainerCreateConfig.Volumes`** — volumes are now list-backed `source:target[:ro]` entries with an explicit `isReadOnly` parameter; the same host directory can be mounted at multiple container paths.
- **`BuildAsync()` with zero `Use*` calls now throws `InvalidOperationException`** instead of silently returning empty results (`FluentDockerException` remains the type for docker-level validation failures; empty build is a programming error).
- **Removed dead public API**: `ComposeServiceBuilder`, `ImageBuilderConfig`, `NetworkWithAlias`.
- **`BuildResults.GetContainer/GetNetwork/GetVolume`** — lookups are now case-sensitive (`Ordinal`; docker names are case-sensitive) and annotated as nullable.
- **Removed `FluentDocker.Testing.Core.Plugins`** — the unused resource-plugin host (`ITestResourcePlugin`, `TestResourcePluginHost`) is deleted; no in-repo or known external consumers existed.
- **`ITestResource.InitializeAsync` failure contract** — all non-cancellation initialization failures are now uniformly wrapped in `ResourceInitializationException` (carrying `Diagnostics`); catch blocks matching concrete inner exceptions (e.g. `TimeoutException`) must catch the wrapper and inspect `InnerException`.
- **Removed dead Model DTO/config APIs** — deleted unused create/config types (`ContainerCreateParams*`, `ServiceCreate`, `NetworkCreateParams`, `Model.Images.Image`, `NetworkConfiguration/NetworkRow`, `*BuilderConfig`, `CertificatePaths`, `ImageConfig`, `Ulimit*`).
- **Model public-surface cleanup** — `HostIpEndpoint` is now a JSON DTO, the `EmbeddedUri.Host` alias was removed (use `Assembly`), and `HealthState` / `Orchestrator` enum ordering gained sentinel values.
- **`KernelBuilder` is single-use** — a second `Build()` / `BuildAsync()` call now throws; create a new builder for another kernel.
- **`IDriverRegistry.Unregister` / `UnregisterAsync` throw `DriverNotFoundException` on an unknown driver id** — previously a silent no-op. This matches `GetDriver`'s hard-error contract; guard idempotent teardown with `IsRegistered(id)` before unregistering.
- **`CliPruneOutputParser` moved from namespace `FluentDocker.Drivers` to `FluentDocker.Drivers.Docker.Cli`** (source-breaking) — it is a Docker/Podman CLI adapter helper, not port-contract surface. Update `using`s; the shared byte parsing now lives in `CliByteParser`.
- **Removed `IBinaryResolver.MainDockerCompose`** — the legacy `docker-compose` (v1) binary was discovered but never executed (compose always runs as the `docker compose` subcommand). The dead property is gone; `ResolveBinaryPath` resolves the `compose` binary type to the main Docker client (identical path).
- **Removed `XunitContainerFixtureBase.SkipWhenUnavailable`** — xUnit cannot skip from fixture initialization; use `IsDockerAvailableAsync()` with `Assert.SkipWhen` in the test body.
- **Model Runner endpoint validation tightened** — `UnixSocket(path)` requires an explicit path, `WithEndpoint(...)` and `WithInferenceDriver(...)` are mutually exclusive, and model digest validation is stricter.
- **Podman validation is no longer silent** — `UsePod(...)` now throws on non-Podman drivers, and leading-dash positional names are rejected.
- **Nullable reference types** — the core `Model`, `Extensions`, and `Resources` namespaces (and almost all of `Common`) are now null-annotated (`#nullable enable`). Genuinely optional members are `T?`; the rest are non-null. Code compiled with nullable enabled may surface new warnings where it previously ignored `null`.
- **Timestamps are `DateTimeOffset`** — `Container.Created`, `ContainerState.StartedAt`, `ContainerState.FinishedAt`, and `Volume.Created` changed from `DateTime` to `DateTimeOffset`; the engine's UTC offset is now preserved instead of discarded.
- **Enum values renumbered** — `RuntimeType` and `DriverType` now start at `Unknown = 0`. Numeric enum values are **not** a stable contract — serialize by name, never persist or transmit the number.
- **`ComposeServiceDefinition.Isolation` is now `ContainerIsolationTechnology`** — the old `ContainerIsolationType` enum was removed.
- **Removed types** — `ContainerIsolationType`, `ContainerSpecificConfig`, `ProcessRow`, `Processes`, and `PreferredDriverType` (all unused or superseded).
- **Moved to `FluentDocker.Kernel`** — `CapabilityChecks`, the `KernelCapabilityExtensions` extension methods (including `kernel.EnsureCapabilityAsync(...)`), and the `DriverCapability` enum moved from `FluentDocker.Common`. Update `using FluentDocker.Common;` to `using FluentDocker.Kernel;` — the extension-method move is otherwise a silent build break.
- **Moved to `FluentDocker.Kernel`** — `BuildResults` and `BuildScope` moved from `FluentDocker.Model.Kernel` to `FluentDocker.Kernel`. Update imports to `FluentDocker.Kernel` — the type move is source-breaking for build-result call sites.
- **`ContainerBuildParams` is now `[Obsolete]`** — test-only; slated for removal.
- **Docker API portless URIs default to standard HTTP(S) ports** — a `DockerHost` given as `http://host` (no port) now defaults to `:80` and `https://host` to `:443`, matching the scheme; previously both fell back to `:2375`/`:2376`. Explicit `tcp://host` still defaults to `2375` (plain) / `2376` (TLS). Set the port explicitly to pin the old behavior.

### Added

- **Docker Model Runner (local LLMs)** — first-class support for managing and consuming local LLMs through Docker Model Runner, behind the existing `Builder → WithinDriver → UseModelRunner()` pattern. Highlights:
  - **`IModelRunner` façade** composing small capability interfaces (`IModelStore` for pull/ls/inspect/rm/tag/push/package/df/prune, `IModelEngine` for status/version/ps/load/unload/configure/logs/install, `IModelInference` for chat/completion/embeddings), plus ergonomic `ChatAsync` / `ChatStreamAsync` / `EmbedAsync` bound to a default model.
  - **CLI management/runtime adapters** (`docker model …`) and an **HTTP inference adapter** speaking the OpenAI-compatible API on `:12434` (TCP or unix socket), with **SSE streaming** (`[DONE]` termination, mid-stream fault → `ModelRunnerException`, cancellation). **Model management (pull/list/remove/inspect) is CLI-based; there is no native HTTP `/models*` management API in this release.**
  - **`ModelReference`** value object (Docker Hub `ai/…`, Hugging Face `hf.co/…`, fully-qualified registries, digests) and **`LlamaCppRuntimeFlags`** typed, range-validated flag builder.
  - **Managed `IModelService`** (`IServiceAsync`) that loads on `StartAsync`, unloads on `StopAsync`/dispose, participating in the same state-machine + hook pipeline as containers.
  - **`WithModel(...)` container extension** injecting `LLM_URL`/`LLM_MODEL` and a host-gateway alias (no network/volume created), and **`ModelRunnerEnvironment.FromEnvironment()`** to reconstruct a runner from injected env vars.
  - **`GenericOpenAiModelRunner`** targeting any OpenAI-compatible endpoint (with optional bearer token).
  - **Split control and data planes** — inference is HTTP-only (no transport selector; the `docker model` CLI cannot stream or embed, so `DockerCliDriverPack` composes the HTTP inference adapter and owns its connection). `IModelRunnerBuilder.WithEndpoint(...)` repoints inference at a different address, and `WithInferenceDriver(IModelInferenceDriver)` / `WithInferenceDriver(string driverId)` run inference on an explicit driver or another registered driver's inference port while management/runtime stay on the scoped driver.
  - New driver ports `IModelManagementDriver` / `IModelRuntimeDriver` / `IModelInferenceDriver`, and `ErrorCodes.Model` / `ErrorCodes.ModelInference` groups.
  - DMR-availability-gated integration tests (tiny `ai/smollm2` / `ai/embeddinggemma`) that skip cleanly when the runner is absent, and `ModelRunnerBenchmarks`.
  - **Optional `IModelBackendInfo` capability** — a model driver MAY advertise its inference backend engine(s) (e.g. `llama.cpp`, `vllm`); the runner sources `Capabilities.DefaultBackend` / `AvailableBackends` from it (or reports none) instead of assuming one.
  - **Reusable OpenAI inference adapter** — the OpenAI-compatible HTTP inference adapter is `OpenAiModelInferenceDriver` (namespace `FluentDocker.Drivers.Models`), a runtime-neutral type non-Docker runner plugins can reuse directly (vLLM, LM Studio, hosted endpoints) by registering it under `IModelInferenceDriver` in a custom `IDriverPack`.
- **`IDriverRegistry.UnregisterAsync(...)`** — async unregister support for driver registry cleanup.
- **`INetworkService.GetConnectedContainersAsync(...)`** — returns connected container names/IDs across Docker CLI, Docker API, and Podman.
- **Container-owned port resolution** — `IContainerService` exposes async host-port endpoint helpers using the docker-host-aware resolver.
- **Lifecycle additions** — `UnpauseAsync` is available on container/compose services, and `ComposeService.RemoveAsync` removes compose projects through the service surface.
- **Model Runner transport knobs** — added `AllowApiKeyOverInsecureTransport` and `StreamFirstByteTimeout`.
- **`FLUENTDOCKER_REAP_RUNNING_AFTER`** — opt-in env var (e.g. `24h`, `30m`, `2d`; a bare number means hours) that lets the orphan sweep reclaim crash-leaked **running** managed test containers older than the ceiling. Unset, invalid, or `<= 0` turns it off (previous behavior: running foreign-session containers were never reaped). It is the sole age gate for running containers and is independent of `minimumAge`.

### Changed

- Deleted `HttpExtensions` / `OsExtensions`; shared HTTP lifetime now goes through `Common.SharedHttpClient`.
- Promoted culture/comparison analyzers to errors to prevent locale-sensitive parsing regressions.
- `ModelReference` canonicalizes explicit `docker.io/` references to the default registry identity.
- Podman machine `Memory` / `DiskSize` report values are normalized to bytes.
- DMR TLS semantics are explicit: absent `ca.pem` uses system trust, present `ca.pem` pins exclusively, and missing/partial PEM configuration throws.
- Port waits floor each per-attempt connect budget at 2 seconds.
- `IContainerBuilder` gains `WithExtraHost(host, ip)` (used by `WithModel` for the Engine host-gateway alias).
- `DockerCliDriverPack` registers the model ports (`IModelManagementDriver` / `IModelRuntimeDriver` / `IModelInferenceDriver`); `PodmanCliDriverPack` registers no model ports (RamaLama pack is future work). For portable/driver-agnostic code, prefer `TryUseModelRunner(out IModelRunnerBuilder runner)` to degrade gracefully on drivers that lack model support (e.g. Podman) instead of `UseModelRunner()`, which throws `InterfaceNotSupportedException`.
- CLI log/event/stat streaming now throws `DriverException` (`ErrorCodes.Driver.CommandExecutionFailed`) on non-zero process exit instead of ending silently.
- **Inference adapter renamed/relocated (preview, source-breaking).** `DockerApiModelInferenceDriver` (`FluentDocker.Drivers.Docker.Api.Components`) → `OpenAiModelInferenceDriver` (`FluentDocker.Drivers.Models`): it speaks generic OpenAI-over-HTTP and is not Docker-specific. Done while the subsystem is preview, so no released API breaks.
- **Backend is driver-sourced, not hardcoded.** `ModelRunnerCapabilities.DefaultBackend` / `AvailableBackends` now come from the resolved driver via `IModelBackendInfo` (the Docker CLI runtime driver reports `llama.cpp`); custom packs report their own backend or none, and `GenericOpenAiModelRunner` reports none — no plugin is misreported as `llama.cpp`. Note: an explicit inference override (`WithEndpoint(...)` / `WithInferenceDriver(...)`) sources the backend from the override alone — repointing inference at an arbitrary endpoint reports no backend rather than assuming the scoped runtime's engine (the server behind a repointed URL is unknown and may not be `llama.cpp`).
- **Clean `FluentDockerNotSupportedException` for partial packs.** The kernel-backed `IModelRunner` now throws `FluentDockerNotSupportedException` (naming the missing capability) — not the lower-level `InterfaceNotSupportedException` — when a store/engine op is invoked on a driver pack that registers only some model ports (e.g. an inference-only plugin), matching `GenericOpenAiModelRunner`. `FluentDockerNotSupportedException` derives from `FluentDockerException`, not `System.NotSupportedException`. `Capabilities` remains the programmatic check.
- **Top-level `Builder` is driver-scoped.** After `WithinDriver(...)`, `Builder` implements `IDriverScopedBuilder`, so portable model code can use `TryUseModelRunner(out var runnerBuilder)` directly and degrade gracefully on drivers without model ports.
- **Docker CLI foreground `RunAsync` classifies success by container creation, not stderr text.** Non-detached runs discover the container via `--cidfile`: a container that was created and ran to completion returns `Success = true` and surfaces its exit code on `ContainerRunResult.ExitCode` (now `int?`; null when detached and for the Podman / Docker API drivers), even when that exit code is non-zero. Pre-flight docker failures that never create a container (invalid reference, conflicting options, unknown flag → exit 125) return `Fail(CreateFailed)`. **Breaking for callers that treated any non-zero run as failure via `.Success` alone — review `RunAsync` call sites.**
- **Exec-form healthchecks route through `CMD-SHELL`.** A `--health-cmd` supplied as an argv array is joined into one string and run by Docker via `/bin/sh -c`, so it requires `/bin/sh` in the image and fails on distroless / `scratch` images. Use an image that ships a shell, or drive the probe from a binary the healthcheck can exec directly.
- **`ModelApiConnectionConfig.CertificatePath` requires an `https` endpoint.** Configuring client certificates against a plaintext `http` model-runner endpoint now throws `ArgumentException` instead of silently rewriting the scheme to `https`.
- **Chat message content is now multimodal-safe.** `ChatMessage.Content` (string) became a convenience text projection over a new `ChatMessage.RawContent` (`System.Text.Json.JsonElement?`, serialized as the `content` field). Inbound multimodal / array content (e.g. `image_url` parts) is preserved verbatim on round-trip instead of being flattened to text and dropped; set `RawContent` to send multimodal / non-text content. The `ChatMessageContentConverter` type was renamed `ChatMessageRawContentConverter`.
- **Compose process listing.** `ComposeProcesses.ContainerId` may now be `null` (name-join fallback) and a new `ContainerName` field is populated. `ContainerListFilter.Limit` now implies all-states (via `--last`) and ignores values `<= 0` (Docker CLI and Podman).

### Fixed

- **Compose builds without `WithProjectName` now work end-to-end.** The CLI driver no longer fabricates a `default` project name after `up`; when unset, `ps`/`logs`/`exec`/`down` identify the project via the compose files, so `ListServicesAsync` finds compose's derived-name project and dispose actually tears it down (previously both silently targeted a nonexistent `default` project).
- **Non-streaming inference preserves `EndpointUnreachable`.** A transport failure (connection refused / DNS / socket error) on `ChatAsync` / chat / completion / embeddings / engine-model list now surfaces `ErrorCodes.ModelInference.EndpointUnreachable` instead of being downgraded to `RequestFailed`, matching the streaming path and the documented error contract.
- **`EmbeddingsRequest` is deep-copied before send.** A copy constructor was added and the driver copies the request, so mutating the caller's `Input` list after the call can no longer alter the wire body (parity with chat/completion).
- **Bounded inference error-body read.** A non-success inference response body is read with a 64 KiB bound instead of fully materializing a hostile/oversized error body before truncation.
- **`ModelReference` registry case normalized.** The registry host is lowercased at parse time so value-equal references (registry hosts are case-insensitive) always serialize identically — stable dictionary keys and emitted CLI args.
- **Windows mTLS client certificates.** Client certs loaded from PEM are re-imported with a persisted key on Windows (SChannel rejects ephemeral-key client-auth certs); non-Windows behavior is unchanged.
- **Compose `ConnectToExisting` uses borrowed semantics.** Disposing a connected compose service releases the local handle only; it does not run `docker compose down` against the existing project.
- **Docker CLI production hardening.** Fixed Docker 29 timestamp/output-shape parsing plus stdout/stderr separation, UTF-8 stdin, broken-pipe diagnostics, sudo/binary quoting, compose exec/run exit semantics, output truncation, and top/kill/list parsing.
- **Docker API production hardening.** Fixed streaming demux/cancellation, bounded copy spool, safe filter JSON, long stop/restart timeouts, build registry auth headers, keepalive/dispose races, exec-inspect, `.dockerignore`, IPv6 host-IP, and tar mtime handling.
- **Docker API driver adversarial hardening (prod-readiness).** API version negotiation now clamps to `min(daemon, client-max)`, fails fast below a `1.24` floor, and recovers after a transient `/_ping` failure (pins the client default for the current request but keeps re-negotiating, so a daemon that later reports an older version is no longer permanently mis-versioned); large request-body uploads (`/build`, `/images/load`, `/images/create?fromSrc`) are no longer spuriously cancelled by the header-read timeout; cert-implied TLS with no explicit port defaults to `2376`; multiplexed log demux buffers lines across frames instead of splitting them (and no longer degrades to O(n²) on newline-dense output) and uses the stream `Content-Type` (API ≥ 1.42) to skip a per-call TTY inspect; directory copy-out extracts atomically via a staging directory; `.dockerignore` accepts Go's `[^…]` character-class negation; and cached registry credentials are cleared on driver-pack dispose.
- **Podman production hardening.** Fixed Podman 6 `podman ps` JSON keys, pod parsing, and machine report normalization.
- **Podman CLI driver adversarial hardening (prod-readiness).** Event timestamps and machine memory/disk sizes now parse with the invariant culture (a non-Gregorian host locale such as `th-TH` no longer shifts event times by ~543 years, and locale-specific sign/format no longer zeroes numeric sizes); null label/option/annotation/DNS/port/volume collections no longer risk an NRE across the pod/image/network/volume/kubernetes/machine drivers (the container driver's null-collection guard is now shared on the base driver); `podman system info` no longer reports conmon's version as the engine version when the podman `version` block is absent; and `podman load` parses the legacy (≤ 4.0) comma-joined `Loaded image(s):` line into individual image references.
- **Service-layer hardening.** Corrected `IServiceCapabilities`, mapped `RestartPolicy` / `CpuQuota`, wired dispose remove-volume options, made unexposed-port waits time out instead of throw, and prevented remove-then-dispose hook replay.
- **Docker 29 compatibility and hardening wave.** Folded the production-readiness remediation across Common, Kernel, drivers, services, builders, testing, and docs into the preview.
- **Missing `docker model` plugin is detected and reported.** A model operation against a daemon without the plugin fails with error code `MDL_024` and a message to install `docker-model-plugin` or enable Docker Desktop's Model Runner, instead of surfacing the raw CLI error.
- **`DOCKER_MODEL_RUNNER_URL` preserves its query string** (e.g. `?key=x`) when building the endpoint. A value that is set but not a valid absolute `http(s)` URL logs a warning and falls back to host TCP `:12434` rather than throwing (the `Try*` env parse honors the BCL no-throw contract).
- **`ModelService.StartAsync` is re-armable.** The load-once gate resets when a start fails, is cancelled, or the model is stopped, so a failed or stopped model can be started/reloaded again (previously a wedged load-once flag could block every retry).
- **Model Runner (DMR) adversarial hardening (prod-readiness).** Streaming inference now fails with `ModelInference.StreamParseError` when the SSE stream produces zero events (an empty or non-`text/event-stream` body, e.g. a proxy error page) or ends without a `[DONE]` terminator (a truncated/mid-stream-closed response), instead of silently reporting a successful empty or partial completion; `ModelService.StartAsync` waiters now honour their own `CancellationToken` (a cancelled caller abandons only its wait and gets an `OperationCanceledException`) while the single shared load still runs to completion under `CancellationToken.None` and keeps the per-model gate held — a wedged `docker model run` no longer blocks every caller forever; the `ChatAsync(prompt)` convenience now reports "no text content (e.g. tool_calls or a refusal)" rather than falsely claiming "no choices" when a tool-calling/refusal response carries choices but no message text; and the reachability ping reads response headers only (`ResponseHeadersRead`) instead of buffering the full `/models` body.
- **Builder API adversarial hardening (prod-readiness).** HTTP-URL wait conditions no longer double-delay between retries (the inter-attempt `WithRetryDelay` was being applied twice per failed probe, roughly doubling the effective wait budget); `WithStartupTimeout(int)` was added so the container start budget can be set independently of any wait condition; borrowing an existing network or volume (`ReuseIfExists` / `ConnectToExisting`) now logs a warning when `RemoveOnDispose` / delete-on-dispose is also requested, since the borrowed resource is intentionally not torn down; images and pods referenced before they are declared in the same build are now rejected at build time with a clear ordering error (mirroring the existing network/volume check) instead of failing opaquely at runtime; container ports that differ only by protocol case (`80/TCP` vs `80/tcp`) are de-duplicated; `WithinDriver("")` and `WithWaitTimeout(negative)` are rejected instead of silently accepted; env-file keys and unquoted values are trimmed (compose/`godotenv` parity); the inverted `ReuseIfExists` XML doc was corrected; and the Podman fluent builder gained the same `UseModelRunner` / model passthroughs as the Docker builder.
- **Service-layer adversarial hardening (prod-readiness).** A `StateChange` handler can now re-enter lifecycle operations (stop/remove) without self-deadlocking: every service (container, network, volume, image, pod, compose, and model) snapshots state under its lock and invokes handlers *after* releasing it, so a handler that calls back into the service no longer blocks on the lock it is already holding — handler delivery order is consequently not guaranteed under concurrent transitions and this is now documented on `IServiceAsync.StateChange`; disposed-instance guards on the container service are keyed on dispose *completion* rather than dispose *entry*, so teardown hooks that run during `Dispose`/`DisposeAsync` still observe the live container (read ops such as `GetLogsAsync`) while post-dispose external callers still get `ObjectDisposedException`; `IContainerService.ExecuteDetailedAsync(...)` was added to return the full `ExecResult` (exit code plus stdout/stderr) instead of only a boolean; stopping an already-`Removed` container or pod is a no-op that can no longer resurrect it (mirroring the existing `RemoveAsync` idempotency guard), and compose `RefreshStateAsync` likewise refuses to resurrect a removed project from an empty `ps`; best-effort temp-file/-directory cleanup after copy/export no longer masks the operation's real success or failure; `host.docker.internal` DNS probes are cancellable and bounded by a 3-second timeout (`IsDockerDnsAvailableAsync` / `GetDockerHostAddressAsync` gained `CancellationToken` overloads); in-container detection additionally recognizes the `/run/.containerenv` marker and `containerd` / `libpod` cgroup entries (Podman / containerd / cgroup-v2 hosts); and the service endpoint resolver now selects the first parseable host binding instead of failing on a leading unparseable one.
- **Test-support / testing-libraries adversarial hardening (prod-readiness).** The MsTest fixtures (both the per-test `MsTestContainerFixtureBase` and the class-scoped `MsTestClassContainerFixtureBase`) and the NUnit fixture now mark a test **inconclusive / ignored** — rather than failing it — when `SkipWhenUnavailable` is set and the runtime binary is missing (`DriverNotAvailableException`, e.g. no `docker` / `podman` on `PATH`), matching the wrapped-`FluentDockerUnavailableException` skip they already honoured; every `ITestResource` now runs a uniform runtime-health preflight *before* provisioning, so a resource created against a down runtime fails fast with `FluentDockerUnavailableException` (**behavioral change**: non-container resources — volume, network, image, model, topology — previously surfaced a raw mid-provision error when the daemon was unavailable); an abandoned late provision that only lands *after* disposal is now force-removed on the disposal path, and the process-wide orphan-cleanup fallback records the resource name **only when that force-remove itself fails** (e.g. the kernel is already disposed) — a successful teardown no longer leaks the name or risks wrong-reaping a later same-named resource; the illusory per-fixture initialization locks in the xUnit fixtures were removed (a single throwing re-entry guard already covered them); and [ADR 0001](docs/adr/0001-testing-core-in-fluentdocker-package.md) documents why the `Testing.Core` runtime ships inside the main `FluentDocker` package.
- **Model Runner inference now surfaces HTTP 200 error envelopes.** A `200 OK` body of the form `{"error":{"message":"…"}}` from an OpenAI-compatible runner is now returned as a failure (`ErrorCodes.ModelInference.RequestFailed`) instead of a spurious empty success.
- **Service lifecycle state notifications are now edge-triggered.** The `StateChange` delegate fires only when a service transitions to a *new* state, so repeat operations that do not change state no longer re-notify. A failed operation transitions the service to `Unknown` and runs any registered `Unknown` hooks. Handlers run inline before the error propagates, so they must not block on the failure path.

### Security

- Hardened Docker API build-context tar packing against symlink escape, dangling-link host-file exfiltration, and `symlink/..` realpath traversal.
- DMR refuses bearer API keys over insecure non-loopback transports unless `AllowApiKeyOverInsecureTransport` is explicitly enabled.
- CLI/Podman leading-dash guards reduce option-injection risk for positional names.

### Known issues

- Nullable reference types are fully annotated (`#nullable enable`) across the `Model`, `Extensions`, and `Resources` namespaces and almost all of `Common`; the `Kernel`, `Drivers`, `Services`, and `Builders` layers still run under the project's `<Nullable>annotations</Nullable>` context (compiler warnings deferred).
- Podman sudo-password redaction in `PodmanBinary` logging remains future work.

## [3.1.0] - 2026-06-04

### Added

- **Container `KillAsync`** — `IContainerService.KillAsync(signal = "SIGKILL")` for fast, forceful teardown of disposable containers without a graceful stop (#267)
- **Compose `RestartAsync`** — `IComposeService.RestartAsync()` and `RestartAsync(services)` to restart a whole project or individual services (#318)
- **Builder interactive/entrypoint options** — `WithInteractive()`, `WithTty()`, and `WithEntrypoint(...)` to keep short-lived images alive for `ExecuteAsync` and to override the entrypoint (#264)
- **In-place image builds** — `DockerfileBuilder.WithBuildContext(...)` builds an existing Dockerfile in place via the engine's `--file`, leaving no generated `Dockerfile` behind (#280)
- **Attach to an existing compose project** — `IComposeBuilder.ConnectToExisting()` returns a service bound to a running project without `docker compose up`, plus `IComposeService.RefreshStateAsync()` to read live state (#305)
- **Custom container CLI** — `IDockerCliDriverBuilder.WithBinary("finch"/"nerdctl", ...)` drives docker-compatible engines without aliasing them to `docker` (best-effort) (#315)
- **Log source tagging** — `IStreamDriver.StreamLogEntriesAsync(...)` yields `LogEntry` values tagged with `LogStreamSource` (stdout/stderr); the Docker Engine API driver reports the real source from the multiplexed stream (#326)

### Changed

- **`ExecuteOnRunning` / `ExecuteOnDisposing` semantics** — each string argument is now executed as a **separate** command (restoring the v2 contract); Running commands run **after** wait conditions, exactly once, and command failures now surface instead of being swallowed (#283)

### Fixed

- **`GetConfiguration` inspect parsing** — `NetworkSettings.LinkLocalIPv6PrefixLen`, `GlobalIPv6PrefixLen` and `IPPrefixLen` are emitted by the engine as JSON numbers; a tolerant `string` converter prevents the `DriverException: Failed to inspect container` regression introduced by the System.Text.Json switch (#335)

### Documentation

- Added an "Inspecting Container Info" section showing how to read the created date, image config, environment, exposed ports, labels, and the mapped host port (#197)

## [3.0.1] - 2026-05-11

### Fixed

- Centralized the 3.0.1 version in `Directory.Build.props`, fixed pages workflow issues, and reclassified Docker-dependent tests as integration.

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
- **Nullable annotation context** — `<Nullable>annotations</Nullable>` enabled across all projects (full `#nullable enable` warnings are rolled out per-namespace over the 3.x line)
- **.NET 8 + .NET 10** multi-targeting *(net8.0 dropped in 3.2.0-preview.2 — the library now single-targets net10.0)*

### Deprecated

- `IService` (sync) — use `IServiceAsync` instead; sync methods wrap async with `.GetAwaiter().GetResult()` which can deadlock
- `FluentDocker.Model.Containers.CommandResponse<T>` — use `FluentDocker.Model.Drivers.CommandResponse<T>` instead
- `FluentDocker.Services.NetworkCreateConfig` — use `FluentDocker.Drivers.NetworkCreateConfig` instead
- `IFeature`, `FeatureAttribute`, `FeatureConstants` — v2 legacy types, will be removed in a future release

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
