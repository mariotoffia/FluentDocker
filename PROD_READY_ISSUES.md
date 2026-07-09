# PROD_READY_ISSUES

Adversarial production-readiness review of FluentDocker (v3.2.x, `featrure/model-support`, HEAD `05b2613f`
"Drop net8.0 target; single-target net10.0"). Supersedes the previous review at `3b18aad3`; every prior
finding was re-verified against current HEAD rather than carried over.

Ten independent adversarial subagent reviews — one per driver, one per functional area. Each chunk states
what was examined and lists findings ordered critical → minor. Questions evaluated per area:

1. Production ready? 2. .NET 10 best practices? 3. Documentation production ready? 4. Zero bugs?
5. Resilient (outages, recovery)? 6. Easy to consume/understand? 7. Docs findable/readable vs bloated/fragmented?

Baseline at review start: `make build` clean (0 errors), full unit suite green on net10.0.

## Executive summary

**Totals: 1 CRITICAL, 27 MAJOR, ~61 MINOR** across ten areas (three findings independently discovered by two
reviewers each — cross-confirmed, counted once per owning chunk: CopyCommand injection, UnpauseAsync doc
contract, graceful-skip docs gap).

**Overall verdict:** the codebase is in genuinely strong shape — the prior hardening passes (`3b18aad3`,
`f03f54c7`) verifiably hold at HEAD, arg-quoting/process-lifecycle/SSE-parsing/teardown layers repeatedly
survived adversarial probing, and reviewers confirmed solid areas explicitly. No finding is a
crash-in-happy-path, data-loss, or exploitable-security class. What blocks "production ready" is a short,
concrete list:

**Ship-gate (fix before tagging):**
1. `CHANGELOG.md` missing the net8.0-drop breaking change (CRITICAL, Chunk 10).
2. Paused containers flip to `Running` via every inspect path — state corruption + bogus events (Chunk 2).
3. `PushAsync` false success on cleanly-truncated stream — silent CI/CD failure mode (Chunk 4).
4. Multimodal chat contract: impossible in both directions yet documented as round-tripping (Chunk 5).
5. Silent localhost fallback on malformed `DOCKER_MODEL_RUNNER_URL` — wrong-runner inference (Chunk 5).
6. `Unregister/UnregisterAsync` unbounded dispose hang; pack `TryResolve` faults escape `SysCtl` raw (Chunk 7).
7. COPY `--chown`/`--from` Dockerfile instruction injection (Chunks 3+8).
8. README `--prerelease` silent port-swap install trap; `volumes.md` infinite-loop sample;
   `NotSupportedException` misnomer; `UnpauseAsync` doc contract (Chunks 2+10).

**Fix-soon (majors that mislead operators or leak resources):** pod/manifest error-code bypass (Chunk 1);
discovered-container `Unknown` states (Chunk 2); `JsonHelper` Try-contract throw + `BridgeNetwork` IPv6 null
kills inspect + `FileExtensions.Copy` phantom path (Chunk 3); compose `ImagesAsync` `Ok(empty)` on parse
failure + `Network.IPv6` never populated by inspect (Chunk 9); wait-condition `timeoutMs ≤ 0` trap +
`WithBuildContext` silently ignored + invisible compose-teardown failure (Chunk 8); graceful-skip feature
family undocumented (Chunks 6+10); remaining doc majors (Chunk 10).

**Per-area verdicts (Q1 production ready):** Podman CLI — yes w/ caveat · Services — conditional ·
Common/Model — yes w/ 2 reservations · Docker API — yes · Model Runner — yes w/ 2 reservations ·
Testing libs — yes · Kernel — conditional · Builders — conditional · Docker CLI — yes · Docs — conditional.

---

## Chunk 1 — Podman CLI Driver

**Examined:** All 41 files under `FluentDocker/Drivers/Podman/**` read in full — `PodmanCliDriverBase` + 8
partials (ProcessStart, Buffering, Input, Context, Failures, Streaming, Unbounded, Attach), `PodmanCliDriverPack`
+ Lifecycle, all 20 Cli/Components drivers (+Args/Operations/Parsing/StatsParsing partials),
`PodmanContainerParser`, Binary/ (5 files), `PodmanContainerExtensions`, all `IPodman*` interfaces;
`Services/Impl/PodService.cs`; `Kernel/PodmanCliDriverBuilder.cs`; `Common/CommandLineQuoting.cs`;
`Model/Drivers/ErrorCodes.cs`; `docs/podman.md`. Parity-compared against `Drivers/Docker/Cli/**`. Ran 614
Podman-scoped `Category=Unit` tests — all pass. All files ≤500 lines.

**Verdicts:**
1. Production ready: **YES, with one MAJOR caveat** (error-code inconsistency, Finding 1). Execution engine
   genuinely hardened: bounded output (4 MiB cap, 256 KiB rolling tails), 5-min default buffered timeout,
   sudo password via stdin only, cidfile-based run-id extraction with cancellation cleanup, machine auto-start
   under per-name locks with bounded readiness polling.
2. .NET 10 best practices: **YES** — `SearchValues<char>`, collection expressions, primary constructors,
   `ConfigureAwait(false)`/`CancellationToken` throughout, `IAsyncDisposable`, bounded `Channel(256)` streaming.
3. XML docs: **YES — exemplary.** Remarks document real operational semantics (stdout/stderr merge ordering,
   podman 4-vs-5 flag spellings, truncation markers); match verified behavior in every cross-check.
4. Zero bugs: **NO** — 1 major + 2 minor. No critical; no argument-injection path found
   (`CommandLineToArgvW`-compatible quoting verified; leading-dash guarded via `QuotePositionalArgument`).
5. Resilient: **YES** (modulo Finding 1). Machine-down detected and mapped to retryable `Machine.NotRunning`;
   auto-start re-polls `podman info` up to 60s; hung processes bounded with process-tree kill; cancellation
   rethrows OCE and cleans up cidfile containers within a 5s budget.
6. Easy to consume: **YES.** `WithPodmanCli(...)` is a drop-in swap for `WithDockerCli(...)`; unsupported
   surfaces (Compose/Swarm/Stack) fail loudly with honest capability flags; `podman-docker` shim trap documented.
7. Docs: **YES.** `docs/podman.md` is 173 lines, TOC'd, honest, accurate (version constraints, output caps,
   progress-callback reality). Not bloated, not fragmented.

### Findings

**[MAJOR] `Drivers/Podman/Cli/Components/PodmanCliPodDriver.cs:37` (also 68, 97, 126, 155, 182, 209, 240, 277,
308) and `PodmanCliManifestDriver.cs:43` (also 77, 117, 153, 193, 231, 273) — CLI-failure paths bypass
`FailureCode`, breaking machine-down classification.**
Every other component driver (Container/Image/Network/Volume/System/Auth/Kubernetes — e.g.
`PodmanCliVolumeDriver.cs:46`) wraps the non-exception failure path in `FailureCode(result.Error, fallback)`,
which maps daemon-connection errors to retryable `Machine.NotRunning` (machine-managed) or
`Api.ConnectionFailed`. Pod and Manifest drivers return the bare fallback (`ErrorCodes.Pod.StopFailed`,
`ErrorCodes.Manifest.PushFailed`, …) on `!result.Success`; only their `catch` blocks use `FailureCode(ex, …)`.
Consequence: with the podman machine stopped, `podman pod stop` fails with `Pod.StopFailed` instead of
`Machine.NotRunning`, so any consumer keying retry/auto-start/diagnostics on the machine-down code gets
inconsistent behavior across 17 operations. Copy-paste divergence; one-line fix per site.

**[MINOR] `Drivers/Podman/Cli/Binary/PodmanBinariesResolver.cs:116-127` — `WithBinary("podman-remote")` can
never succeed and fails with a misleading error.**
`Make` classifies any discovered file named `podman-remote` as `PodmanBinaryType.PodmanRemote`, and
`MainPodmanClient` (line 37) only accepts `PodmanClient`-typed binaries. Explicitly configuring
`d.WithBinary("podman-remote")` hits the ctor throw at 40-48: `DriverNotAvailableException("Failed to find
podman client binary")` — even though the binary *was* found. Either honor the explicitly-configured name as
the main client or reject it with an accurate message.

**[MINOR] `Drivers/Podman/Cli/Components/PodmanCliContainerDriver.Args.cs:141-148` — `HealthCheck.Test =
["NONE"]` emits `--health-cmd NONE` instead of `--no-healthcheck`.**
The OCI/Docker convention `Test: ["NONE"]` means "disable the image's healthcheck", but the arg builder only
special-cases `CMD`/`CMD-SHELL`, so `NONE` falls through to `--health-cmd NONE` — a healthcheck running the
literal command `NONE` that perpetually fails, flipping the container to `unhealthy`. The Docker CLI driver
(`DockerCliContainerDriver.Args.cs:119-127`) has the identical gap (also reported in its chunk). Map
`["NONE"]` to `--no-healthcheck` (supported by both CLIs).

**Verified non-issues:** podman JSON casing/shape differences (PascalCase keys, array vs NDJSON, unix-seconds
`Created`) handled by dedicated parsers with unit coverage; `--entrypoint` correctly uses podman's JSON-array
form; capability advertisement honest (`SupportsCompose=false`, pods/machines/manifests/kube true); streaming
stdin redirection is exact Docker-driver parity and harmless.

---

## Chunk 2 — Services Layer (excl. Model Runner)

**Examined:** `FluentDocker/Services/**` in full — `IServiceAsync`, `ServiceRunningState`, `StateChangeEventArgs`,
`StateChangeNotifier`, `ServiceHookExtensions`, `IServiceCapabilities`, all service interfaces; `Impl/` —
`ContainerService` + Lifecycle/Operations/Export partials, `ImageService`, `NetworkService`, `VolumeService`,
`ComposeService` + 4 partials, `HostService` + Operations, `PodService`, `EngineScope`; `Extensions/` —
`ServiceExtensions` (+Logs/Sync), `ServiceEndpointResolver`, `EnvironmentExtensions`. Supporting: CLI driver
inspection/list mapping, `ErrorCodes.IsTransientCode`, `SharedHttpClient`, `docs/service-lifecycle.md`,
`docs/containers.md`. Verification: 683 service unit tests pass; live docker 29.5.3 probes (paused-container
inspect semantics; `docker stop` on paused). ModelRunner/ModelService excluded (own chunk).

**Verdicts:**
1. Production ready: **CONDITIONAL YES.** Dispose idempotency (Interlocked CAS), cancellation discipline,
   transient-error retry in waits, error→exception mapping are production-grade. Two MAJOR state-machine bugs
   (paused→Running flip; discovered-container state parsing) should be fixed first — both small fixes.
2. .NET 10 best practices: **YES.** `IAsyncDisposable`-first with safe sync bridge, `ConfigureAwait(false)`
   everywhere, `Stopwatch.GetTimestamp` monotonic cache TTL, `System.Formats.Tar`, shared `HttpClient`.
3. XML/API docs: **MOSTLY YES.** Interface docs excellent; gaps on borrowed-handle `RemoveAsync`,
   `CopyToAsync(byte[])` directory destinations, one false statement in `service-lifecycle.md`.
4. Zero bugs: **NO** — 3 major, 6 minor below.
5. Resilient: **LARGELY YES.** Daemon outage → transient `Api.ConnectionFailed` → waits retry until timeout;
   external container death → gone→`Removed` with correct stale-inspect versioning; dispose fencing correct.
   Caveats: waits never fail fast on container exit; `WaitForPortAsync` docker-proxy false positive.
6. Easy to consume: **YES.** Small documented state machine, events only on real transitions, honest sync
   facade. Wrinkles: hooks skip inspect-driven transitions; no `Created` state (reports `Starting`).
7. Docs: **YES.** `service-lifecycle.md` (166 lines) and `containers.md` (590 lines) task-oriented and honest;
   one stale claim, one missing caveat.

### Findings

**[MAJOR] `Services/Impl/ContainerService.cs:439-441` — Paused containers mapped to Running by every inspect path.**
`data.State.Running == true → ServiceRunningState.Running` short-circuits before `ParseState(Status)` can
return `Paused`. Docker reports paused containers as `Running=true, Paused=true, Status="paused"` (verified
live on 29.5.3). After `PauseAsync`, any inspect past the 500 ms cache TTL — `GetConfigurationAsync`,
`WaitForPortAsync`, endpoint resolution — flips state Paused→Running and fires a bogus `StateChange(Running)`;
a subsequent `PauseAsync` passes its state guard, hits the daemon's "already paused" error, and lands the
service in `Unknown` with a `DriverException`. Same pattern at `:191-193` (StartAsync post-inspect) and
`:266-269` (UnpauseAsync fallback). Fix: `Running == true && Paused != true`. No unit test exercises
`Paused = true` inspect data.

**[MAJOR] `Services/Impl/HostService.cs:419-425` — Discovered non-running containers get state Unknown,
contradicting the code's own promise.**
`GetContainersAsync` → `ParseContainerState(state.Status)` — but the CLI list driver
(`DockerCliContainerDriver.Inspection.cs:152-157`) puts the **human-readable** `docker ps` text in `Status`
(e.g. `"Exited (0) 2 minutes ago"`) and the keyword in `dto.State`, which is discarded except for `Running`.
Exact-match parsing → every stopped/paused/restarting discovered container seeds as `Unknown`. The comment at
`HostService.cs:190-192` explicitly claims this path prevents mislabeling — it mislabels as `Unknown` instead
of `Stopped`/`Paused`. Fix: map from the keyword field.

**[MAJOR] `docs/service-lifecycle.md:46,149` — Documented UnpauseAsync error contract is false at HEAD.**
Doc: "`ContainerService.UnpauseAsync` is the exception: on failure it throws without changing state." Code
(`ContainerService.cs:280-284`): the catch does `UpdateState(Unknown); throw;` like every other lifecycle
method. Recovery logic written per the doc (twice instructed) gets `Unknown` instead. Restore the special-case
or fix the doc.

**[MINOR] `Services/Impl/ContainerService.cs:426-456` — Inspect-driven state transitions fire events but skip hooks.**
`ApplyInspectResultIfVersionCurrent` and gone→`Removed` raise `StateChange` but never run `AddHook` hooks;
hooks run only inside lifecycle methods. `service-lifecycle.md` says hooks "run when the service reaches a
given state" — a hook on `Stopped` won't fire for an externally-killed container. Document or unify.

**[MINOR] `Services/Impl/ComposeService.Remove.cs:11-16` — Borrowed compose handles report Removed without
tearing anything down.**
When `_downOnDispose=false` (from `ConnectToExisting`), `RemoveAsync` skips `compose down`, deletes temp
files, and sets `Removed` while the project still runs. Ownership documented for Dispose, not explicit
`RemoveAsync`. Document the no-op or throw for borrowed handles.

**[MINOR] `Services/Impl/HostService.cs:283-288` — Created-never-started containers report Starting indefinitely.**
`ParseState("created")` → `Starting`; `ServiceRunningState` has no `Created`. A merely-created container shows
a transition-looking `Starting` forever, muddying `StartAsync`'s already-`Starting` guard. Document or add a state.

**[MINOR] `Services/Extensions/ServiceExtensions.cs:108-155` — WaitForPortAsync: userland-proxy false positive
and no fail-fast on container death.**
TCP connect success against a published port proves docker-proxy accepts, not that the app listens — on
default installs it can return `true` before readiness; neither code nor `containers.md` mentions this. All
wait helpers also spin the full timeout when the container has already exited instead of failing fast.

**[MINOR] `Services/Impl/HostService.Operations.cs:57-104` — PullImageAsync composes malformed ref for tagged
image + explicit tag.**
`PullImageAsync("nginx:1.25", tag: "1.26")` composes `nginx:1.25:1.26`; drivers' `ShouldAppendTag` only
protects digest refs. Fails loudly but blames Docker rather than the API misuse. Validate or document precedence.

**[MINOR] `Services/Impl/ContainerService.Operations.cs` (CopyToAsync byte[] overload) — Directory destination
silently yields a random filename in the container.**
Data staged via `Path.GetTempFileName()` then `docker cp`ed; if `containerPath` is an existing directory the
file lands as `<dir>/tmpXXXX.tmp` — silent non-deterministic misplacement. Document file-path requirement or
reject directories.

---

## Chunk 3 — Common Utilities, Extensions, Resources & Model (DTO) Core

**Examined:** All in-scope files read in full at HEAD — `Common/**` (37 files: JsonHelper,
JsonElementExtensions, Lenient{Bool,Int32,StringDictionary,StringList}Converter, TolerantStringConverter,
ShellArgParser, CommandLineQuoting, CliOutputParser/Truncation, DirectoryHelper, FdOs, SharedHttpClient,
RequestResponse, ModelEnvName, ModelOperationGate, ErrorContextExtensions, exceptions, obsolete
Result/Option/FdEvent surface), `Extensions/**` (8), `Resources/**` (6), `Model/**` DTOs excl. `Model/Models/**`
(Containers, Images, Networks, Volumes, Events, Stacks, Compose ×29, Common, Drivers/CommandResponse,
Builders/FileBuilder incl. DockerfileInstructionGuard/DockerfileJson). Empirical repros under `.out/advrepro/`
against the built net10.0 assembly (JsonHelper non-object roots, BridgeNetwork null fields, EnumerateArraySafe,
TolerantStringConverter). Unit suite: **5183 passed, 0 failed**. Docs: `docs/utilities.md`, `docs/api-reference.md`.

**Verdicts:**
1. Production ready: **YES, with two reservations** — culture-invariant parsing, tolerant converters,
   factory-only `CommandResponse`, traversal-proof resource extraction, atomic temp-file writes; but two
   confirmed JSON-resilience bugs (Findings 1–2) contradict the library's own stated contracts.
2. .NET 10 best practices: **MOSTLY YES.** UTF-8/span overloads present; DTO core deliberately on the
   reflection resolver (required by the tolerant-converter modifier) — a conscious trade, not a violation.
3. XML/API docs: **YES for the hardened surface, NO for legacy corners** — `NoWarn CS1591` hides real gaps
   (`ResourceInfo`, `AddCommand`, `CmdCommand`, some Compose DTO members ship blank into the API reference).
4. Zero bugs: **NO** — 4 major, 6 minor below.
5. Resilient: **MOSTLY YES.** Malformed JSON → default; unknown enums → `Unknown`; no tr-TR/comma-decimal
   hazards (verified incl. SI/binary suffix math); output bounded; traversal unrepresentable — except Findings 1–2.
6. Easy to consume: **YES.** `CommandResponse<T>` coherent (factory-only, `Success` vs exceptions clean,
   `ErrorCode`+`ErrorContext`); legacy surface large but exemplarily `[Obsolete]`-annotated.
7. Docs: **YES.** `utilities.md` (577 lines) accurate against HEAD; `api-reference.md` a deliberate pointer to
   the generated reference. Gap: JsonHelper — the advertised JSON gate — has no utilities.md section.

### Findings

**[MAJOR] `Common/JsonHelper.cs:160-202` — `TryGetProperty`/`TryGetIntProperty` throw `InvalidOperationException`
on valid non-object JSON, violating their Try-contract.**
Both parse input then call `JsonElement.TryGetProperty` on the root; for Array/String/Number roots that throws
`InvalidOperationException`, but the catch covers only `JsonException`. Repro confirmed:
`JsonHelper.TryGetProperty("[1,2,3]", "x")` → throws. XML docs promise "returns false" and recommend the method
for NDJSON streams — exactly where non-object lines occur. Public-API landmine in the JSON gate itself. Fix:
guard `ValueKind == JsonValueKind.Object` or widen the catch.

**[MAJOR] `Model/Containers/BridgeNetwork.cs:25` — `GlobalIPv6PrefixLen` lacks `LenientInt32Converter`; a single
`null` from the engine kills the entire container inspect.**
`IPPrefixLen` (line 20-21) has the lenient converter; sibling `GlobalIPv6PrefixLen` is bare `int`. Repro
confirmed: `{"GlobalIPv6PrefixLen": null}` inside `NetworkSettings.Networks` fails the whole
`TryDeserialize<Container>`, while `IPPrefixLen: null` succeeds as 0. Podman/older daemons emit null IPv6
fields on IPv4-only networks. Inconsistent hardening of two identical-risk fields in the same class.

**[MAJOR] `Model/Builders/FileBuilder/CopyCommand.cs:47-55` — `Chown`/`Alias` interpolated raw into
`--chown=`/`--from=` with no validation; Dockerfile instruction-injection gap.**
`From`/`To` are JSON-escaped via `DockerfileJson.Array`, and every other guarded command routes user input
through `DockerfileInstructionGuard.Validate`, but `Chown`/`Alias` are interpolated verbatim — a value
containing `\n` becomes a new Dockerfile instruction. Caller owns the Dockerfile so not a hard security
boundary, but it is the one unguarded seam in an otherwise systematically guarded builder.

**[MAJOR] `Extensions/FileExtensions.cs:91-116` — `Copy` for directory input copies the directory's *contents*
into the workdir but returns the directory *name* as relative path.**
Line 112 `fd.CopyTo(workdir)` flattens contents into `workdir`; line 115 returns `Path.GetFileName(fd)` — a
path that does not exist there. Any consumer using the returned path (e.g. in a subsequent Dockerfile `COPY`)
references a phantom directory. Un-`[Obsolete]`d public API returning a silently wrong result; no internal callers.

**[MINOR] `Model/Builders/FileBuilder/HealthCheckCommand.cs` — renders `HEALTHCHECK  --interval=…` with a
double space.** Docker parses it; cosmetic defect in generated Dockerfiles and exact-string tests.

**[MINOR] `Model/Compose/ComposeServiceDefinition.cs:416` — `Volumes` is get-only while every sibling
collection property is get/set.** Breaks object-initializer construction and round-trip symmetry.

**[MINOR] `Model/ErrorCodes.cs` — `IsTransientCode` omits `Api.ServerError` (HTTP 500).**
Daemon 500 — the classic transient hiccup — is not retried by helpers keyed on `IsTransientCode`. Defensible
conservatism, but the asymmetry is undocumented.

**[MINOR] `Common/JsonElementExtensions.cs:140-153` — `GetBoolOrDefault` rejects numeric 0/1, inconsistent with
`LenientBoolConverter`.** Same document yields different answers depending on which of the library's own two
access styles is used.

**[MINOR] `Common/JsonHelper.cs:~130` — `ParseElement` throws with no `<exception>` docs, amid a class whose
brand is "never throws".**

**[MINOR] `FluentDocker.csproj:20` + `Resources/ResourceInfo.cs`, `Model/Builders/FileBuilder/AddCommand.cs`
(et al.) — `NoWarn CS1591` masks missing XML docs on public surface** that ships blank into the generated API
reference advertised as covering "every public type".

**Verified non-issues:** no Newtonsoft references remain (doc-comment mentions only); all files ≤500 lines;
Model→Common imports are documented legacy exceptions per AGENTS.md; resource-writer traversal guard and
atomic-move pattern correct; `SudoPassword` `JsonIgnore`d and redacted in `ToString`; unit suite green (5183/5183).

---

## Chunk 4 — Docker API (HTTP) Driver

**Examined:** All 44 files under `Drivers/Docker/Api/**` read in full — `Connection/` (DockerApiConnection +
Tcp/Headers/Negotiation/StreamIdleTimeout partials, DockerApiConnectionConfig, ClientCertificateLoader,
ModelTlsValidation, IDockerApiConnection), `DockerApiDriverBase` (7 partials), `ApiResult`,
`DockerApiDriverPack`, all 23 component drivers (Container ×8, Image ×5 + TarWriter + DockerIgnoreFilter,
Stream ×2, System, Network, Volume, Service, Auth, RegistryAuth), `ApiModels/`, plus
`Drivers/Connection/ResponseOwningStream.cs`, `Model/Common/DockerUri.cs`, `docs/docker-api.md`, and the
49-file test suite. Ran all 579 DockerApi-filtered unit tests at HEAD: **579/579 pass**. Prior remediations
(TTFB timeout, demux frame bounds, TLS validation, URL escaping, error-body bounding, dispose ordering) all
verified present at HEAD.

**Verdicts:**
1. Production ready: **YES** for its documented scope (single-image ops, legacy builder, no
   Compose/Stack/BuildKit); the one MAJOR is a narrow false-success window on push.
2. .NET 10 best practices: **YES, minor gaps.** Exemplary: shared `SocketsHttpHandler` + two `HttpClient`
   facades, `ConnectCallback` for unix/npipe/TLS, TCP keep-alives, `ResponseHeadersRead` streaming,
   `IAsyncEnumerable` NDJSON via `PipeReader`, source-gen JSON on hot paths. Gaps: no `PooledConnectionLifetime`;
   per-read CTS/timer allocation in idle-timeout stream.
3. XML docs: **YES, with caveat** — meaningful summaries everywhere read; `CS1591` in NoWarn means coverage is
   convention, not enforced.
4. Zero bugs: **NO** — 1 major, 10 minor below; no crash/corruption/security bugs.
5. Resilient: **YES.** Daemon restart mid-stream detected (`StreamEnded`, typed demux-truncation errors, never
   silent short data); socket-gone maps to 599 distinct from daemon 5xx; cancellation never masked; negotiation
   re-pins after failure. No automatic retries by design (documented; caller owns retry policy).
6. Easy to consume: **YES.** Uniform `CommandResponse<T>` + machine-readable error codes; all files ≤500 lines
   (max 499). Traps: `IDockerApiConnection` default interface members throw for custom impls; no `docker
   context`/rootless socket resolution.
7. Docs: **YES.** `docs/docker-api.md` (208 lines) accurate against the code — TLS knobs,
   cancellation-vs-timeout, stream bounding, BuildKit/Compose limits all verified true. Honest about limitations.

### Findings

**[MAJOR] `Drivers/Docker/Api/Components/DockerApiImageDriver.Build.cs:283-291` — PushAsync can report success
on a cleanly-truncated stream.**
Push succeeds if ≥1 NDJSON progress line arrived and no `error` line was seen. `PullAsync` (:104-176) was
hardened with terminal-status detection *plus* an `ImageExistsAsync` fallback probe; push verifies nothing
terminal (no digest/`aux` check, no registry probe). A proxy/LB idle-timeout or daemon shutdown closing the
chunked response *cleanly* mid-push yields `Ok` for an image that never fully reached the registry — silent
false success in CI/CD. Fix: require the terminal digest line, mirroring pull.

**[MINOR] `DockerApiDriverBase.ErrorHandling.cs:92` — Mid-stream failures mislabeled "Cannot connect to Docker
daemon".** `DescribeTransportFailure` falls through to 599 "Cannot connect…" for any non-timeout exception, and
`ClassifyStreamException` routes mid-read errors through it — wrong on-call diagnosis. Classify post-connect
stream failures distinctly (e.g. `Api.StreamInterrupted`).

**[MINOR] `Connection/DockerApiConnection.StreamIdleTimeout.cs:50-54` — Per-read CTS+timer allocation;
IOException can mask TimeoutException.** Every `ReadAsync` allocates two linked CTS + a `Task.Delay` timer;
when idle timeout fires, a socket-abort `IOException` can propagate instead of the intended
`TimeoutException`. `Task.WaitAsync(TimeSpan, ct)` is cheaper and deterministic.

**[MINOR] `Components/DockerApiServiceDriver.cs:101` — Unreachable `return Ok` after conflict-retry loop.**
Dead code today; a future refactor could make "ran out of retries" silently return success.

**[MINOR] `Components/DockerApiServiceDriver.cs:422,442` — Swarm job modes reported as "global".**
`ReplicatedJob`/`GlobalJob` (API 1.41+) mislabeled; `Replicas` reads 0.

**[MINOR] `Components/DockerApiNetworkDriver.cs:59-62` (+ `DockerApiVolumeDriver.cs:35-38`) — Inconsistent
transport-error taxonomy across component drivers.** ServiceDriver remaps synthetic 599/408 to
connection/timeout codes; Network/Volume always emit `*.CreateFailed`/`*.PruneFailed` even when the daemon was
never reached. Retry logic keyed on error codes gets inconsistent signals per subsystem.

**[MINOR] `Connection/DockerApiConnection.Tcp.cs:69-96` — No `PooledConnectionLifetime` on the shared handler.**
For `tcp://` hosts behind re-pointable DNS, pooled connections live forever; only idle reaping or keep-alive
death heals failover. Set a finite lifetime (e.g. 5 min) for TCP endpoints.

**[MINOR] `Model/Common/DockerUri.cs:46` + `Connection/DockerApiConnection.cs:380-400` — No docker-context /
rootless socket resolution.** `docker context`, macOS Desktop (`~/.docker/run/docker.sock`), rootless Linux,
colima sockets not discovered — users get 599 unless `DOCKER_HOST` is set. CLI driver inherits contexts
implicitly, so migrating users hit the asymmetry first. Docs don't mention it.

**[MINOR] `Components/DockerApiRegistryAuth.cs:14,130` — Plaintext credentials cached for connection lifetime.**
Inherent to X-Registry-Auth (never logged, verified), but visible in memory dumps. Document.

**[MINOR] `Components/DockerApiImageDriver.cs:116-129` — Image remove/prune bound by the 5-minute buffered
client.** Large removes/prunes can exceed 5 min → synthetic 408 while the daemon-side op continues. Container
wait/stop/restart got long-running exemptions; destructive image ops did not. Document `WithRequestTimeout`.

**[MINOR] Anonymous-object request bodies serialize camelCase (e.g. `DockerApiNetworkDriver.cs:123`,
`JsonHelper.cs:208`) — undocumented reliance on Go case-insensitive unmarshal.** Typed models use explicit
PascalCase `[JsonPropertyName]` (verified correct); ad-hoc bodies go camelCase. Works, but make the wire
contract exact via source-gen context.

**Net assessment:** one of the more rigorously hardened HTTP client layers reviewed — remediations verifiably
hold, deliberate shortcuts are marked and explained in-code, docs tell the truth, 579 focused tests pass. Fix
the push completion check; everything else is polish.

---

## Chunk 5 — Docker Model Runner (DMR) Subsystem

**Examined:** Every file in scope, fully: ports (`IModelInferenceDriver`, `IModelManagementDriver`,
`IModelRuntimeDriver`, `IModelBackendInfo`); transport (`Drivers/Models/Connection/*` — `ModelApiConnection` +
Streaming/Tls/TimeoutContent partials, config, TLS validation, cert loader); SSE parser
(`OpenAiModelInferenceDriver.Streaming.cs`) and driver; CLI adapters (`DockerCliModelDriverBase/
ManagementDriver/RuntimeDriver`, `ModelJsonParser`) incl. streaming exit-code path; value objects
(`ModelReference`, `ModelRunnerEndpoint`, `InferenceModelId`); DTOs (`Model/Models/Inference/*`, `Options/*`);
services (`ModelRunnerService.*`, `ModelService`, `GenericOpenAiModelRunner`, `EngineScope`); builders +
extensions; helpers (`ModelOperationGate`, `ModelEnvName`); all three model-runner docs. Ran the model unit
suite (**1233/1233 pass**) and two scratch repros under `.out/` proving multimodal and tool-call JSON contract
behavior empirically.

**Verdicts:**
1. Production ready: **YES, with two reservations.** No criticals. Transport, SSE parser, lifecycle, CLI
   layers genuinely hardened (bounded buffers, ordered disposal, typed errors, per-model gating, quoted args,
   TLS/API-key guards). Fix the two MAJORs before de-previewing.
2. .NET 10 best practices: **YES.** Pull-based `IAsyncEnumerable` + `[EnumeratorCancellation]`; long-lived
   `HttpClient` over `SocketsHttpHandler` with `PooledConnectionLifetime=2min`, `Timeout=Infinite` +
   per-request linked CTS; stateful-`Decoder` SSE parser (CRLF/CR/LF, BOM, split frames, comments, `[DONE]`
   variants, 1MB bounds); `SearchValues`; all files ≤500 lines.
3. XML docs: **YES, one defect** — clean build, unusually good remarks, but `ChatMessage.AdditionalProperties`
   documents a provably false capability (Finding 1).
4. Zero bugs: **NO** — 2 major, 7 minor. Nothing corrupts state or crashes.
5. Resilient: **YES, largely.** Runner absent → `EndpointUnreachable` with actionable guidance; mid-stream
   death → typed timeout (first-byte 10min / idle 120s, distinct from caller cancellation) or loud truncation
   detection (stream without `[DONE]`/finish_reason throws); malformed SSE → bounded typed `StreamParseError`.
   Gap: Finding 2 undermines misconfiguration recovery.
6. Easy to consume: **YES.** First chat reply in ~10 lines; the fluent chain reads exactly as it executes.
   Caveats: multimodal dead end (Finding 1), `EmbedAsync` silent `[]` (Finding 8).
7. Docs: **YES — not bloated.** One 595-line main guide with logical progression + two small satellite pages
   with parent nav. Error-code table and known-limitations list are exemplary honesty. One stale link.

### Findings

**[MAJOR] `Model/Models/Inference/InferenceCommon.cs:126` — Multimodal content is impossible in both
directions, yet the XML doc claims it round-trips.**
`ChatMessage.Content` is `string?` only. Empirically verified: deserializing OpenAI array-of-parts content
(`"content":[{"type":"text",…}]`) throws `JsonException` — any vision-capable endpoint response breaks;
`ChatChoiceChunk.Delta` reuses `ChatMessage` so streaming too. The documented escape hatch is *actively
blocked*: `AdditionalProperties["content"]` throws `ArgumentException` (reserved-name guard,
InferenceCommon.cs:46,119). Yet the doc at :131-135 says "plus multimodal content … round-trips instead of
being dropped." The library markets "any OpenAI-compatible endpoint (vLLM, LM Studio, hosted)" where vision is
routine. Fix: support `string | ContentPart[]` (polymorphic converter) or correct the doc and fail clearly.

**[MAJOR] `Model/Models/ModelRunnerEndpoint.cs:173-174,194-210` — Invalid `DOCKER_MODEL_RUNNER_URL` silently
falls back to localhost; unreachable-error never echoes the actual target.**
`Default()` = `TryFromEnvironment(out e) ? e : HostTcp()`. A set-but-malformed value (`remote-host:12434`
without scheme parses as scheme `remote-host` → rejected) is indistinguishable from unset: no log, no throw,
traffic silently goes to `http://localhost:12434`. Worst case: a local runner *is* running → inference silently
targets the wrong runner and returns wrong answers with zero error. And `EndpointUnreachable`
(`ModelApiConnection.cs:277-283`) hardcodes "The default is host TCP http://localhost:12434", suggesting the
very env var the user already set — never reporting the actual `BaseAddress` attempted. Fix: throw/log on
set-but-invalid env value; include `BaseAddress` in the unreachable message.

**[MINOR] `Model/Models/ModelRunnerEndpoint.cs:29,55` — `Engine` segment not validated URL-path-safe.**
Only `ThrowIfNullOrWhiteSpace`; `"/engines/" + Engine` flows into the request URI unescaped — `llama.cpp/../admin`
or `a?b` = path traversal/query smuggling on the runner API. Project rule is "value objects immutable **+
validated**". Restrict to a safe token charset.

**[MINOR] `Model/Models/ModelRunnerEndpoint.cs:98` — `Raw()`/`BaseAddress` retains userinfo (credential leak in
diagnostics).** Connection strips it before sending, but `endpoint.BaseAddress` surfaces in caller logs and
exception context. Strip at construction.

**[MINOR] `Model/Models/Inference/ChatCompletion.cs:75` vs `Completion.cs:62` — `Seed` is `long?` on chat but
`int?` on completions.** Same OpenAI parameter, two widths; reserved-key guard blocks the workaround. Align to `long?`.

**[MINOR] `Model/Models/Inference/Completion.cs:44` — `Prompt` is `string?` only; OpenAI accepts
`string | string[]`.** Batch prompts unreachable entirely (reserved key blocks `AdditionalProperties["prompt"]`),
contradicting docs/model-runner.md:166-168. Same polymorphic fix as Finding 1, or document.

**[MINOR] `Drivers/Docker/Cli/Components/DockerCliModelRuntimeDriver.cs:40-41,79-135` — `configure --backend`
support probe cached per driver instance, keyed by first caller's context.** Two daemons with different DMR
plugin versions get one answer. Cache per context host, or document.

**[MINOR] `Services/Impl/ModelRunnerService.Inference.cs:123` — `EmbedAsync` silently returns `[]` on empty
response data, while `ChatAsync` throws on missing content.** Zero-length vectors are garbage downstream
(vector stores, cosine similarity); failure surfaces far from cause. Throw `ModelRunnerException`.

**[MINOR] `docs/model-runner.md:558,595` — Example link hardcodes the (typo'd) feature branch
`featrure/model-support`.** 404s the moment the branch merges/deletes. Use a master-relative link.

**Verified-clean (adversarial checks that found nothing):** SSE framing edge cases incl. EOF decoder flush and
mid-stream `{"error":…}`; cancellation-vs-idle-timeout attribution; stream ownership/double-dispose; truncation
fails loud; CLI arg quoting; `ModelEnvName` as YAML/env injection boundary; API-key-over-insecure-transport
refusal; custom-CA exclusive trust; PFX ephemeral-key round-trip with key-material zeroing; per-model
`ModelOperationGate`; builder disposes runner on build failure; no test hooks in production code; tool-calls
pass through `AdditionalProperties` and `finish_reason: null` chunks parse (empirically confirmed).

---

## Chunk 6 — Testing Libraries & Test-Suite Quality

**Examined:** All 22 `FluentDocker/Testing/Core/*.cs` (6,585 LOC full reads: ResourceBase ×4 partials,
ProcessExitReaper, OrphanCleanup, all 9 resource types, DockerAvailability, DriverSelection, ResourceLifecycle,
ResourceDiagnostics, options/exceptions); all shipped adapter sources in `FluentDocker.Testing.Xunit` (7
fixture/test bases), `.MsTest` (3 bases + helpers), `.NUnit`; both RunnerTests projects; `FluentDocker.Tests`
quality audit — trait census across 505 files (507 Unit / 44 Integration / 13 PodmanIntegration / 12 DevLocal
classes), deep reads of concurrency/failure-semantics/orphan-cleanup/reaper/mock meta-tests; `docs/testing.md`
+ all 7 `docs/testing/*.md` (3,141 lines) cross-checked against code; Makefile/CI targets; packaging. Live
verification at HEAD: MSTest runner **8 passed/3 skipped-by-design**, NUnit runner **2 passed/2
skipped-by-design**, testing-core unit subset **344/344**. `InternalsVisibleTo`: **zero occurrences**. No
`async void`; all files ≤500 lines (max 498).

**Verdicts:**
1. Libraries production ready: **YES** (as preview-labeled packages). Lifecycle core genuinely hardened:
   semaphore-serialized lifecycle, `Interlocked` dispose idempotence, generation-fenced abandoned-provision
   cleanup, force-remove on fresh timeout token, retryable failed teardown, label-based cross-session orphan
   cleanup with 1-hour age guard. Remediations hold and are pinned by non-tautological regression tests.
2. .NET 10 / xUnit-v3-era practices: **YES.** xUnit v3 `IAsyncLifetime`/`ValueTask`,
   `TestContext.Current.CancellationToken`, `Assert.SkipWhen`; MSTest 3.x `ClassCleanupBehavior.EndOfClass`
   handled explicitly with a leak tracker; env-var-mutating tests serialized via dedicated collection.
3. Docs production ready: **MOSTLY — one MAJOR gap** (graceful-skip family undocumented, Finding 1); everything
   checked is accurate (API names, defaults, env vars, honest reaper limits, no stale net8.0 refs).
4. Zero bugs: **no critical or major functional bugs in shipped libraries**; 5 minors. Test-suite defects
   hiding product bugs: **none found** — mocks have meta-tests asserting real-driver parity, runner tests prove
   real MSTest/NUnit/xUnit-v3 runners drive the lifecycles, prod-ready tests assert observable behavior.
5. Resilient: **YES, with documented limits.** Failed init → adapters dispose via `ResourceLifecycle`; teardown
   failure → force-remove fallback + diagnostics + retryable dispose; parallel collisions impossible
   (guid names, no fixed ports); ctrl-C reaping opt-in and honestly documented as catchable-exits-only.
6. Easy to consume: **YES** — verified <10 lines of user code per framework; MSTest class-level boilerplate is
   a platform constraint, documented with rationale and backstopped by a leak-warning tracker.
7. Docs findable/readable: **YES.** Hub + 7 focused sub-pages, parity/lifetime tables, categories guide matches
   Makefile line-by-line; 3,141 lines proportionate. The one findability failure is Finding 1.

### Findings

**[MAJOR] `docs/testing/xunit.md:366`, `docs/testing.md:135` — Shipped graceful-skip feature family is
completely undocumented.**
`SkipWhenUnavailable` (MsTest/NUnit fixture bases) and the entire `XunitConditionalContainerFixtureBase` class
have **zero** mentions across all testing docs (grep verified). The parity table advertises a "conditional
fixture" no page explains, and the DMR docs teach users to *hand-roll* the probe-then-skip pattern the packages
already ship and RunnerTests prove. Consumers on Docker-less machines get hard failures or reimplement a
shipped, tested feature.

**[MINOR] `Testing/Core/ResourceBase.cs:214,228` — Exit-reaper unregistered on failed init while the container
may still exist.** Init catch blocks call `UnregisterReaper()` even when `_provisioned = true`; dispose path is
stricter (unregisters only after cleanup succeeds). With reaper enabled, ctrl-C in the failed-init→DisposeAsync
window escapes the reaper in exactly the scenarios it exists for. Mitigated by adapter dispose + next-run
orphan sweep; deferring unregister would be symmetric and safer.

**[MINOR] `Testing/Core/ResourceBase.cs:259-277` — Concurrent second `DisposeAsync` returns before teardown
completes.** Documented trade-off for the hung-init escape hatch; awaiting the first disposal would be
least-surprise.

**[MINOR] `Testing/Core/ComposeResource.cs:271-273` — Compose label-overlay temp files leak on crash.**
Overlay JSON in `{temp}/fluentdocker/` deleted only in teardown; killed processes accumulate files (no
age-based sweep of that folder). Disk hygiene only.

**[MINOR] `Makefile:41` vs `:44` — `test-mstest` has no category filter, `test-nunit` does.**
Harmless today; the first Integration-tagged MSTest runner test silently makes `make check` require a Docker
daemon, breaking the documented "pre-push gate needs no infrastructure" contract.

**[MINOR] `Testing/Core/ProcessExitReaper.cs:222-225` — POSIX handler relies on default termination;
double-signal semantics untested.** Single SIGINT/SIGTERM works; second signal during the ≤5s cleanup window
unhandled. Acceptable for an opt-in best-effort feature; noted for completeness.

**Positive assurance:** no fixed host ports or raw `Task.Delay` assertions in Unit tests (timing sleeps live
only in Integration/DevLocal); files apparently missing traits are partial-class continuations; Moq loose-mock
nulls inside orphan cleanup are caught and logged, never masking outcomes.

---

## Chunk 7 — Kernel & Driver Registry

**Examined:** All 18 `FluentDocker/Kernel/` files read fully (`DriverRegistry` + Dispose/Helpers,
`FluentDockerKernel`, `KernelBuilder` incl. `DriverBuilder`, `BuildScope`, `BuildResults`,
`BuildFailureManifest`, `CapabilityChecks`, the three driver builders, `ISysCtl`, `IDriverRegistry`,
`IKernelBuilder` + builder interfaces); contracts (`IDriver`, `IDriverPack`, `IDriverInterfaceResolver`,
`DriverPackBase`); exceptions; pack registration seams (`DockerCliDriverPack` 485 L, `DockerApiDriverPack`
316 L, `PodmanCliDriverPack` + Lifecycle — all full); `DriverContext.CloneWith` drift check. Executed:
kernel-scoped unit tests **261/261 pass** (net10.0); a 3-case repro harness under `.out/adv-kernel-repro/`
proving Findings 1–3; grep audits — `ConfigureAwait` complete, `InternalsVisibleTo` none, max file 494 ≤ 500,
CloneWith covers all 17 properties (no drift).

**Verdicts:**
1. Production ready: **CONDITIONAL** — lifecycle core (two-phase reservation registration, rollback, budgeted
   disposal, single-use builder) genuinely hardened; ship after fixing the two MAJORs.
2. .NET 10 best practices: **YES with minor deviations** — `Interlocked`/`Volatile` state machines,
   `ObjectDisposedException.ThrowIf`, collection expressions, documented `Task.Run` sync bridges; deviations:
   async arg-validation via faulted task, inconsistent builder input validation.
3. XML docs: **MOSTLY** — unusually honest ownership/disposal remarks; one proven contract error (`TrySysCtl`
   exception list), two omissions.
4. Zero bugs: **NO** — 2 major + 6 minor; nothing critical.
5. Resilient: **MOSTLY** — registration rollback correct incl. partial pack failure (shared-instance HashSet
   prevents double-dispose); dispose budgets + `AbandonedDriverCount`; `BuildResults` retryable-dispose state
   machine well designed. Gap: unregistration is unbounded (Finding 1).
6. Easy to consume: **YES** — fluent builder, uniform `SysCtl`/`TrySysCtl`, capability helpers, rich
   `DriverNotFoundException` (lists registered IDs). Traps: `TrySysCtl` throw semantics (Finding 3).
7. Docs: **YES** — `architecture.md`, `extensibility.md`, `advanced-drivers.md`, `getting-started.md` all cover
   kernel/SysCtl; not bloated.

### Findings

**[MAJOR] `Kernel/DriverRegistry.cs:181-184` — Unregister/UnregisterAsync dispose without budget or
cancellation; hangs forever on a hanging driver dispose.**
`UnregisterAsync` awaits `DisposeDriverSafelyAsync` with no timeout; its `cancellationToken` only guards the
lock wait, not the dispose. Proven by repro: a driver whose `DisposeAsync` never completes → `UnregisterAsync`
hangs indefinitely; sync `Unregister` (line 148) additionally pins a threadpool thread forever.
`DisposeAsync` (DriverRegistry.Dispose.cs:61-73) defends against exactly this with a 60 s budget +
`AbandonedDriverCount` — unregistration is the inconsistent, unbounded path. Fix: route through
`DisposeDriver[Pack]WithinBudgetAsync` and honor the token.

**[MAJOR] `Kernel/FluentDockerKernel.cs:271` — pack `TryResolve` faults escape `SysCtl` raw, violating the
documented ISysCtl exception contract.**
`TryResolveCore` wraps unexpected faults from the `driverPack.SysCtl` fallback (285-299) into
`InterfaceNotSupportedException`, but `driverPack.TryResolve(...)` at line 271 (and the driver-path resolver at
308) is unguarded. Proven: a pack whose `TryResolve` throws `UriFormatException` → `kernel.SysCtl<T>()` throws
raw `UriFormatException`, though `ISysCtl.cs:31-34` documents only
DriverNotFound/InterfaceNotSupported/IOE/ODE. First-party trigger exists: `DockerCliDriverPack.TryResolve` →
`EnsureInferenceDriver` constructs `ModelApiConnection` lazily inside `TryResolve`
(DockerCliDriverPack.cs:277-310), whose ctor has multiple throw paths (ModelApiConnection.cs:57-65).

**[MINOR] `Kernel/ISysCtl.cs:37-49` — `TrySysCtl` throws `InterfaceNotSupportedException` for unexpected pack
faults; `<exception>` list omits it.** Proven: pack fallback throwing NRE → `TrySysCtl` throws the exact type a
Try-caller expects to be converted to `false`. Use `DriverException` for wrapped faults, or document.

**[MINOR] `Kernel/BuildResults.cs:141-147` vs `FluentDockerKernel.cs:380-386` — two contradictory
concurrent-dispose semantics, one undocumented.** Second `kernel.DisposeAsync()` re-enters and can block ≤60 s
(documented); second `BuildResults.DisposeAsync()` returns immediately while teardown is in flight —
undocumented, untested; teardown-then-assert code will race.

**[MINOR] `Kernel/DockerApiDriverBuilder.cs:44-54` (+CLI/Podman builders) — timeout validation inconsistent;
negatives fail deep in BuildAsync.** `WithStreamIdleTimeout` validates `> 0`;
`WithRequestTimeout`/`WithConnectionTimeout` accept negatives that explode later inside
`DockerApiDriverPack.InitializeAsync` — rollback works but the error surfaces far from the mistake.

**[MINOR] `Kernel/BuildScope.cs:20-24` — primary ctor missing null guards.** `new BuildScope(null, "x")` throws
`NullReferenceException` instead of `ArgumentNullException`; `driverId` also unvalidated.

**[MINOR] `Drivers/Podman/Cli/PodmanCliDriverPack.cs:185,214` — inconsistent pack hardening vs siblings.**
Podman uses `typeof(T).Name` in exceptions vs Docker's generic-safe `TypeNameFormatter.Format`; conversely
Podman's `DisposeAsync` acquires `_initializeLock` before teardown while Docker CLI/API packs dispose the
semaphore without acquiring it — direct-use concurrent Initialize+Dispose can surface
`ObjectDisposedException` from `Release()` in the Docker packs. Align all three.

**[MINOR] `Kernel/CapabilityChecks.cs:155-168` — double default-driver resolution TOCTOU + async arg
validation.** `IsDriverPack(driverId)` then `GetDriverPack(driverId)` each independently resolve null →
default; concurrent `SetDefaultDriver` between calls checks one driver and fetches another. Resolve once.

**Solid areas (verified, no findings):** two-phase reserve/init/commit registration correct under every
constructed interleaving (dispose-during-init, duplicate-ID race, shared-instance reuse);
`KernelBuilder.BuildAsync` failure cleanup (factory-vs-instance ownership, marker-based double-dispose
avoidance) correct; registry dispose 0/1/2 state machine sound; `SudoPassword` redacted.

---

## Chunk 8 — Builders & Fluent API (incl. FileBuilder)

**Examined:** `FluentDocker/Builders/**` at HEAD — `Builder.cs` + partials (BuildOperation, Cleanup,
Validation), `ContainerBuilder` + all 8 partials, `ImageBuilder`, `PodBuilder`, `DockerfileBuilder` + FileOps,
`InternalBuilders` + ComposeOperations (Network/Volume/Compose), `Compose/ComposeModelBuilder` + extensions,
the three fluent driver builders, all builder interfaces, `DriverScopedBuilderExtensions`,
`BuilderModels`/`BuilderInterfaces`, all 19 `Model/Builders/FileBuilder/*`; supporting reads
(`EnvironmentExtensions.WrapValue`, `Kernel/BuildResults`, wait loops, CLI args mapping), docs
(getting-started/containers/compose). Ran 575 builder unit tests (green), scratch repros under `.out/scratch/`
(CMD-null, COPY-chown injection, build-context ignore), live daemon checks (Docker 29.5.3 rejects 1-char
names; Podman accepts them).

**Verdicts:**
1. Production ready: **Conditionally yes.** Core pipeline, teardown, reentrancy, retry genuinely hardened and
   test-covered. Four MAJOR seam issues (one injection vector, three silent-misbehavior traps) to fix; none
   are data-loss or crash-in-happy-path class.
2. .NET 10 best practices: **YES** — collection expressions, primary constructors, `Interlocked`/`Volatile`
   latch, DIMs for non-breaking API growth, sealed internal builders; all files ≤500 lines (max 496).
3. XML docs: **Mostly.** Interfaces richly documented; but CS1591 suppressed and it shows — FileBuilder command
   classes nearly undocumented; one doc states a wrong name pattern.
4. Zero bugs: **NO** — 4 major, 8 minor below.
5. Resilient: **YES — strongest area.** Failure at op N → ops 1..N-1 torn down in reverse with per-service
   bounded CTS; failing container captured via `GetFailedService`; borrowed resources never destroyed; kept
   resources in `BuildFailureManifest` on `ex.Data`; double-`BuildAsync` blocked; retry-after-failure resets
   per-op state; cancellation mid-build still cleans up on bounded tokens. Exceptions: compose blind spot and
   two silent `catch {}` blocks (findings).
6. Easy to consume: **Good, with traps** — scoped wrappers make capability discovery excellent; traps:
   `WithBuildContext`+`FromFile` coupling, `timeoutMs=0`, diverging `WithHealthCheck` defaults, eager-vs-deferred
   validation inconsistency.
7. Docs: **YES.** getting-started (417L), containers (590L), compose (597L) accurate at HEAD (spot-checks
   match code), cross-linked, within the ≤600 rule. Not fragmented.

### Findings

**[MAJOR] `Model/Builders/FileBuilder/CopyCommand.cs:45-53` — COPY `--chown`/`--from` values unsanitized →
Dockerfile instruction injection.**
Verified repro: `new CopyCommand("a","b","root\nRUN evil")` renders `COPY --chown=root` + newline + `RUN evil
["a", "b"]` — the newline splits the line and `RUN evil` becomes a real instruction. Every other instruction is
hardened (`DockerfileInstructionGuard`, `WrapValue`, `DockerfileJson`); this is the one unguarded seam,
reachable via `DockerfileBuilder.Copy(...)`. Reject control chars/whitespace like the other guards.
*(Independently found by the Common/Model reviewer — cross-confirmed.)*

**[MAJOR] `Builders/ContainerBuilder.WaitConfiguration.cs:23-130` — wait-condition `timeoutMs ≤ 0` accepted
silently, fails at build with misleading "Timeout waiting for…".**
No wait method validates `timeoutMs`; 0/negative reaches `WaitForPortAsync` whose loop never runs → immediate
`false` → `FluentDockerException("Timeout waiting for port …")` after zero probes (same for
Http/LogMessage/Process/Healthy). Inconsistent with `WithStartupTimeout` (throws on ≤0). A typo'd
`WaitForPort("5432/tcp", 0)` costs a debugging session; validate ≥1.

**[MAJOR] `Builders/DockerfileBuilder.cs:44-45,189-193` — `WithBuildContext` silently ignored unless `FromFile`
is also set.**
`IsInPlaceBuild = _buildContext != null && !string.IsNullOrEmpty(_config.UseFile?.Rendered)` — with fluent
commands or `FromString`, a configured `WithBuildContext(dir)` is dropped without error: the build runs from a
temp staging dir, so `Copy()` sources the user expects to resolve against their context dir either fail or
match earlier-staged files. A set-and-ignored builder setting should throw at execute time.

**[MAJOR] `Builders/InternalBuilders.ComposeOperations.cs:127-130` + `Builder.cs:253-260` — failed compose
teardown is invisible: swallowed without logging and absent from BuildFailureManifest.**
`CleanupFailedComposeAsync`'s `catch { /* best effort */ }` drops the `down` failure with no log — if `up`
partially creates a stack and `down` also fails (daemon unhealthy — precisely the correlated case), the stack
leaks with zero trace. `UseCompose` also wires no `GetFailedService`, so a failed compose never appears in the
manifest, unlike containers. Log the swallow and manifest the compose project.

**[MINOR] `Model/Builders/FileBuilder/CmdCommand.cs` / `EntrypointCommand.cs` / `ShellCommand.cs` — null/empty
command renders `CMD [null]`.** Verified. Invalid Dockerfile reaches the daemon and fails far from the call
site; `RunCommand`/`WorkdirCommand`/`HealthCheckCommand` guard, these three don't.

**[MINOR] `Builders/ContainerBuilder.cs:150-155` — `WithLinks(null)` throws NullReferenceException.**
`params string[]` binds null → `foreach` NREs without a parameter name; `WithLink(null)` surfaces later as a
less-precise pre-flight error. Add `ThrowIfNull`.

**[MINOR] `Builders/ContainerBuilder.cs:96` + `InternalBuilders.cs:46` — eager configure-time validation
contradicts the "validate at execute time" project rule.** `UseImage` parses immediately; `WithSubnet`
validates eagerly while gateway/IP-range validate at execute. Inconsistent failure surface.

**[MINOR] `Builders/ContainerBuilder.Validation.cs:9-11` + `IContainerBuilder.cs:41` — name regex
over-restricts Podman; XML doc states wrong pattern.** Regex requires ≥2 chars — matches Docker daemon
(verified) but Podman accepts single-char names (verified live), so valid Podman configs are rejected
client-side. The `WithName` doc omits the `+`, so doc and code disagree either way.

**[MINOR] `Builders/ContainerBuilder.cs:175-188` vs `DockerfileBuilder.cs:397-402` — `WithHealthCheck` retries
defaults diverge (0 = inherit daemon default vs explicit 3).** Same method name, different semantics; CLI
driver silently drops negative retries too. Document the 0-means-default contract or align.

**[MINOR] `Builders/ContainerBuilder.Execute.cs:179` — container-remove failure during own-failure cleanup
swallowed without logging**, while the equivalent `Builder.Cleanup.cs` path logs. One `LogDebug` fixes the
observability asymmetry.

**[MINOR] `Builders/ContainerBuilder.Basic.cs:86-104` — `WithVolume` containerPath colon validation only covers
`:ro`/`:rw` suffix.** `/data:z/x` concatenates into a 3-segment volume string → cryptic daemon error instead of
a clear client-side `ArgumentException`.

**[MINOR] `Model/Builders/FileBuilder/*` — public FileBuilder API mostly missing XML docs (CS1591 suppressed).**
IntelliSense on the Dockerfile-generation surface is blank compared to the excellently documented builder
interfaces. *(Cross-confirmed by the Common/Model reviewer.)*

---

## Chunk 9 — Docker CLI Driver

**Examined:** All 44 files under `Drivers/Docker/Cli/**` read in full — `DockerCliDriverBase` (9 partials incl.
Execution, Unbounded, Streaming, StreamSources, Buffering, Input, Attach, Context), `Binary/` (4),
`DockerCliDriverPack` (2), all 27 component drivers (container ×6, image ×3, compose ×4, network, system ×2,
stream, volume, auth, stack, service, model-management, model-runtime, model base), `Components/Parsing/*`
(`ModelJsonParser`, `DockerCliJsonLineParser`, `DockerCliTimestampParser`, `CliByteParser`,
`CliPruneOutputParser`). Traced supporting layer: `CommandLineQuoting`, `CliOutputTruncation`,
`CliOutputParser`, `JsonHelper`; cross-checked `IContainerDriver`/`IStreamDriver`/`INetworkDriver` contracts.
Executed: `dotnet test --filter FullyQualifiedName~Driver.Docker` → **1376/1376 pass**; quoting + parser suites
→ **82/82 pass**. Docs: `advanced-drivers.md` full, spot-checks of architecture/containers/docker-api.

**Verdicts:**
1. Production ready: **YES — ship it**; the two MAJORs are wrong-data bugs in secondary read APIs, not
   stability or security risks.
2. .NET 10 best practices: **YES** — `SearchValues`, `GeneratedRegex`, bounded `Channel`s, collection
   expressions, no Newtonsoft, async-first throughout.
3. XML docs: **YES — unusually strong**: truncation markers, timeout defaults, exit-code semantics,
   cancellation contracts, even probe-cache poisoning semantics documented at interface level.
4. Zero bugs: **NO** — two data-correctness bugs, two doc/behavior mismatches; nothing exploitable.
5. Resilient: **YES — verified.** Daemon-down → `Api.ConnectionFailed`; hung process killed by 5-min default
   timeout (`Kill(entireProcessTree:true)` + reader drain); 64 MiB fail-fast / 256 KiB-tail / 1 MiB-line caps;
   non-zero exits preserved with infra-vs-container disambiguation; mid-stream cancellation tree-kills and
   rethrows caller OCE; faulted backend-probe cache self-evicts; `--cidfile` cleanup on cancelled `run`.
6. Easy to consume: **YES** — uniform `CommandResponse<T>` + error-code taxonomy; caveat: sparse `ListAsync`
   results (Finding 4).
7. Docs: **YES, lean** — task-oriented pages ≤600 lines; gap: `BinaryName` (finch/nerdctl) exists only in XML
   docs, absent from markdown.

### Findings

**[MAJOR] `Drivers/Docker/Cli/Components/DockerCliComposeDriver.Info.cs:161-168` — `ImagesAsync` JSON-array
parse failure silently becomes `Ok(empty)`.**
The array branch catches the deserialize exception, logs Debug, and returns `Ok([])`, while the NDJSON branch
two lines below (171-177) correctly returns `Fail` — whose own contract states an all-failed parse must not
become `Ok(empty)`. Compose format drift (its JSON shapes change often) would silently report "stack has zero
images" to callers gating on `Success`. Fix: return `Fail(..., ErrorCodes.Compose.ImagesFailed)` on
array-branch parse failure, matching the NDJSON branch.

**[MAJOR] `Drivers/INetworkDriver.cs:252-253` (+ `DockerCliNetworkDriver.cs:201`) — `Network.IPv6` never
populated by `InspectAsync`.**
`docker network inspect` emits `"EnableIPv6"`; the model's only property is `IPv6` (matching `network ls
--format json`'s string key). So `ListAsync` reports `IPv6=true` while `InspectAsync` on the same network
always yields `IPv6=false` — silently contradictory data from the authoritative call. Fix: map `EnableIPv6`
(alias property or converter).

**[MINOR] `Model/Drivers/DriverContext.cs:86-91` (+ `DockerCliDriverBase.Context.cs:34-35`,
`.Execution.cs:55-67`) — per-operation `BinaryName`/`SearchPaths` is dead config with a misleading XML doc.**
The doc says "set to finch/nerdctl" — works only at pack registration (resolver built once).
`CreateEffectiveContext` merges per-call overrides, but `ResolveBinaryInfo` resolves the const `"docker"`
through the init-time resolver and never reads them. Document "registration-time only" or honor the override.

**[MINOR] `Drivers/IContainerDriver.cs:152-158` (+ `DockerCliContainerDriver.Inspection.cs:136-159`) —
`ListAsync` returns sparse `Container` without disclosure.**
Only `Id`/`Image`/`Name`/`Created`/`State` populated; `Ports`/`Labels`/`Mounts`/`Networks` are parsed into
`DockerPsDto` then discarded. Interface doc says just "List of containers" — consumers hit an N+1
`InspectAsync` surprise. Document the populated subset or map the ps fields.

**[MINOR] `docs/*.md` — docker-compatible binary support (finch/nerdctl via `BinaryName`) undocumented in
markdown docs.** Discoverable only through XML docs; one paragraph in `advanced-drivers.md` closes the gap.

**Overall:** no criticals. Argument quoting airtight at every traced interpolation site
(`QuoteArgumentIfNeeded`/`QuotePositionalArgument` with leading-dash rejection; sudo and registry passwords via
stdin, never argv); process lifecycle (UTF-8 on all five spawn paths, tree-kill, drain-before-throw,
channel-writer-complete-first) is exceptionally carefully engineered. The prior hardening holds at HEAD.

---

## Chunk 10 — User-Facing Documentation

**Examined:** README.md, CHANGELOG.md, DEVELOPMENT.md, docs/index.md + 23 guide pages, docs/testing/ (7),
docs/migrate-v2-to-v3/ (3), docs/adr/0001, docs/_config.yml + _includes/preview-banner.html, Examples/ (README
+ all 7 projects), .github/workflows/{ci,pages}.yml. Verified ~250 doc API claims symbol-by-symbol against
source (all 11 recent breaking changes hunted: `WithPort` flip, `WaitForHttpUrl` rename, `WithVolume
isReadOnly`, namespace moves, removed types, DateTimeOffset, xUnit `SkipWhenUnavailable` removal, compose
semantics, model-runner renames). Ran a GitHub-slug-accurate link/anchor checker over all 40 markdown files.

**Verdicts:**
1. Production ready overall: **YES, conditionally.** Accuracy exceptional (zero uncompilable snippets in ~250
   checks; all recent breaking changes correctly reflected). Four ship-gate items block "ready" at HEAD:
   CHANGELOG omits today's net8.0 drop; two docs name an exception type that never fires; one sample loops
   forever; README steers preview users into a silent-port-swap install.
2. Teaches modern practice: **YES.** Every guide snippet models `await using` + `BuildAsync()`; sync paths
   flagged deadlock-prone; xUnit v3 current. Gaps: getting-started never shows a `CancellationToken`; all 7
   Examples dispose the kernel with sync `using`.
3. Snippet accuracy: **YES, 4 exceptions** (Findings 2–5).
4. Zero doc bugs: **NO, but remarkably close** — 0 broken relative links, 0 dead anchors across 40 files,
   0 stale TFM/package names in prose; remaining bugs are semantic + 6 URLs pinned to a typo'd branch.
5. Resilience guidance: **YES, unusually strong.** troubleshooting.md error-code tables all real (verified in
   ErrorCodes.cs); daemon-down/TLS/machine-down/timeout paths covered. Weak spot: exception-*type*-first
   guidance missing; only one `IsTransient` retry example.
6. Consumable from docs alone: **YES** — README Quick Start → getting-started → index is one coherent path.
   Only trap is the `--prerelease` install line (Finding 6).
7. Findability/structure: **GOOD, not bloated.** index.md is a real hub (tiers + role reading plans, all
   anchors resolve); model-runner trio is a clean parent/children split; migration.md vs migrate-v2-to-v3/
   duplicates consistently but contradicts itself once (Finding 7); README breaking-changes duplicates
   CHANGELOG verbatim — in sync today, a divergence hazard tomorrow.

### Findings

**[CRITICAL] `CHANGELOG.md:8-39` — net8.0 target drop absent from the release's Breaking section.**
HEAD (05b2613f, today) dropped net8.0 → single-target net10.0, touching README/docs/CI — but not CHANGELOG.
The `[3.2.0-preview.2]` Breaking list has no TFM entry; the last targeting statement (CHANGELOG.md:168) claims
".NET 8 + .NET 10 multi-targeting". README.md:205's breaking section omits it too. Dropping a supported
platform is the most consequential consumer-facing break of this release: a net8.0 user gets a bare NU1202
restore failure with zero documented notice. Must be added before tagging — DEVELOPMENT.md:48's own checklist
("keep CHANGELOG versions aligned") is currently violated.

**[MAJOR] `docs/model-runner-plugins.md:99` (also `model-runner.md:366`, `CHANGELOG.md:74`) — documented
`NotSupportedException` is actually `FluentDockerNotSupportedException` (not a subclass).**
Code throws `FluentDockerNotSupportedException` (ModelRunnerService.cs:165,170,175;
GenericOpenAiModelRunner.cs:342), deriving from `FluentDockerException : Exception` — not
`System.NotSupportedException`. A `catch (NotSupportedException)` written from these docs never fires; the
documented partial-pack degradation recipe is uncatchable as written.

**[MAJOR] `docs/volumes.md:390` — backup sample busy-waits forever on cached `State`.**
`while (backup.State == ServiceRunningState.Running) { await Task.Delay(100); }` polls a cached field updated
only by library-initiated lifecycle calls — no background watcher exists. The `tar` container exits on its own,
the cache stays `Running`, the loop never terminates. Compiles; hangs. Needs `GetConfigurationAsync` inside
the loop.

**[MAJOR] `docs/service-lifecycle.md:148` (and diagram at :46) — false claim: container `UnpauseAsync` failure
leaves "state unchanged".** Code does `UpdateState(Unknown); throw;` like every sibling. *(Independently found
by the Services reviewer — cross-confirmed.)*

**[MAJOR] `docs/architecture.md:67` — Docker CLI output cap documented as 4 MiB; actual cap is 64 MiB.**
`DockerCliDriverBase.Execution.cs:31` sets a 64 MiB buffered-stdout bound; 4 MiB is the stderr bound and the
*Podman* non-streaming cap. Capacity-planning guidance off by 16× for the driver it names.

**[MAJOR] `README.md:38` — "For 3.2 previews, add `--prerelease`" while no 3.2 preview is on NuGet → silent
port-swap install.**
`dotnet add package FluentDocker --prerelease` today resolves **3.1.0** — whose `WithPort` is container-first —
while every doc teaches host-first 3.2 semantics. getting-started.md:29 qualifies it correctly; the README
install section does not. One-line fix, live user-facing copy now.

**[MAJOR] `docs/migration.md:215` vs `docs/migrate-v2-to-v3/test-migration.md:352` — contradictory xUnit
migration targets, risky one demonstrated.**
migration.md prescribes `XunitContainerFixtureBase` + `ConfigureContainer` ("without sync-over-async
constructors"); test-migration.md demonstrates `XunitContainerFixture` with
`InitializeAsync(...).GetAwaiter().GetResult()` in the constructor — a pattern its own tip (line 437) flags as
deadlock-prone. The primary sample is the one the library's design warns against.

**[MAJOR] `docs/testing/xunit.md` — post-removal skip recipe and preflight exception undocumented.**
CHANGELOG.md:28 removed `SkipWhenUnavailable`, telling users to use `IsDockerAvailableAsync()` +
`Assert.SkipWhen` — but `IsDockerAvailableAsync` appears nowhere in the testing docs, and
`FluentDockerUnavailableException` — the documented fail-fast preflight's catchable type — has zero mentions
across docs/. The migration recipe the changelog mandates cannot be found. *(Converges with the Testing
reviewer's Finding 1.)*

**[MINOR] `README.md:166,177`, `docs/migration.md:465`, `docs/model-runner.md:558,595`, preview-banner.html —
six user-facing links pinned to typo'd transient branch `featrure/model-support`.** Work today, 404 after
merge. DEVELOPMENT.md:49 tracks the sweep — deliberate deferral, flagged so it isn't forgotten.

**[MINOR] `docs/model-runner.md:371-413` — `MDL_024` missing from error tables; `TryUseModelRunner` (the
CHANGELOG-recommended portable pattern) taught only in getting-started, never in the three model-runner docs.**

**[MINOR] `Examples/*/Program.cs` — kernel disposed with sync `using` against async-first teaching.**
All 7 examples use `using var kernel` although `FluentDockerKernel : IAsyncDisposable` and every doc teaches
`await using`. Works; contradicts the headline message in the showcase code. Otherwise Examples are clean:
current API, no flipped `WithPort`, all net10.0.

**[MINOR] `docs/containers.md:24` / `docs/compose.md:25` — "canonical usings" blocks incomplete**
(missing `System.Collections.Generic` / `System.Linq` for later samples) — only compiles under ImplicitUsings,
undercutting the docs' own explicit-usings promise.

**[MINOR] `docs/images.md:511` — redundant explicit `DisposeAllAsync()` inside `await using` scope,
contradicting networking.md:491 guidance.** Safe (idempotent) but teaches a confusing pattern.

**[MINOR] `docs/architecture.md:310,386` — enum/capability listings missing newest members**
(`DriverType.Unknown`, `DriverCapabilities.SupportsModels` — the release headline); service-lifecycle.md:132
omits the `StateChange` after-lock/no-ordering contract CHANGELOG says is "now documented".

**[MINOR] `docs/troubleshooting.md:16` — error-code-first, exception-type-blind.**
Never names `DriverNotAvailableException`/`DriverNotFoundException` as catchable types; single `IsTransient`
example. Cosmetic pair: utilities.md:341 argv detail; docker-api.md:179 "256 KiB" for a 256K-chars constant.

**[MINOR] `docs/testing/xunit.md:308,599` + mstest.md:225 + nunit.md:303 + migration-from-legacy.md:387 —
stale "plugin" wording** surviving the deletion of `FluentDocker.Testing.Core.Plugins`. Wording only.

**Bottom line:** the strongest doc suite reviewed against its own source — ~250 spot-checked API claims all
match current signatures, zero broken links in 40 files. **Not yet tag-ready**: add the net8.0 drop to
CHANGELOG/README, fix the exception misnomer, the volumes.md infinite loop, the unpause claim, the 64 MiB
figure, and the `--prerelease` trap — hours, not days, of work.

---
