# Production Readiness Issues

Generated: 2026-07-01

Scope: adversarial multi-agent audit of FluentDocker production readiness. No fixes were applied. `make build` and `make check` passed, but the findings below are contract, resilience, documentation, and release-readiness blockers that unit checks do not prove away.

Overall verdict: **not production ready**. Zero bugs: **no**.

## Cross-cutting blockers

- Stable release automation can publish APIs that the README calls "in development".
- Core lifecycle has a compose disposal data-loss bug and multiple cleanup paths that can hang or leak resources during daemon outages.
- The documented Docker CLI and Podman builder paths contain contract-breaking behavior.
- Docker API private-registry auth, response disposal, cancellation, and stream truncation are not production-safe.
- Model runner timeout, configure concurrency, endpoint truthfulness, and SSE handling have production-impacting gaps.
- Docs are findable, but duplicated and contradictory enough that consumers can copy broken or unreleased examples.

## Docker CLI driver

Examined: `Builder.UseContainer -> BuildAsync -> ContainerBuilder.ExecuteAsync -> SysCtl<IContainerDriver> -> DockerCliContainerDriver.CreateAsync/StartAsync -> ContainerService -> DisposeAsync`, `FluentDocker/Drivers/Docker/Cli/**`, Docker CLI builder/registration, shared ports, Docker CLI tests, README, and docs.

Verdict: **not production ready**. Core flow exists, but advertised fluent options, log/progress contracts, and documentation are not trustworthy enough for production consumers.

### Critical

None found.

### Major

- **[D1] Builder container options are silently dropped by Docker CLI create.** `ContainerBuilder.cs:411-457` populates options such as `ExtraHosts`, aliases, capabilities, security opts, tmpfs/devices, platform/runtime, interactive/tty, and entrypoint; `DockerCliContainerDriver.cs:37-129` renders only a subset for create, while richer `Run.cs:90-193` is not used by `BuildAsync`. Impact: public fluent APIs become production no-ops. Smallest fix: share one complete create-arg renderer and test it through the builder path.

- **[D2] Image progress is a false contract and long output can fail valid operations.** `IImageDriver.cs:25-63` exposes `IProgress<T>`, docs promise it in `docs/architecture.md:429-445`, but `DockerCliImageDriver.cs:31-41`, `65-73`, and `148-162` never report progress and use buffered execution; `DockerCliDriverBase.Buffering.cs:24-36` hard-fails stdout over 4 MiB. Impact: CI builds/pulls can appear hung or fail from normal verbose output. Smallest fix: stream progress/output or remove the progress contract/docs until implemented.

- **[D3] `follow=true` log APIs can hang string-returning calls.** Container logs reject follow in `DockerCliContainerDriver.Inspection.cs:182-186`, but compose/service logs add `-f` and call buffered execution in `DockerCliComposeDriver.Args.cs:117-121`, `DockerCliComposeDriver.Info.cs:57-61`, and `DockerCliServiceDriver.cs:324-333`. Impact: callers get a hanging API that eventually times out/cancels instead of a stream. Smallest fix: reject follow here too or route to streaming APIs.

- **[D4] Lifecycle hook command API/docs contradict implementation.** `IContainerBuilder.cs:441-449` says `params string[]` is command plus args, and `docs/containers.md:315-340` uses `.ExecuteOnRunning("psql", "-U", ...)`; implementation executes each array element separately in `ContainerBuilder.WaitConditions.cs:143-148` and `ContainerService.cs:453-458`. Impact: documented setup/cleanup hooks fail in real containers. Smallest fix: join args as one command or change API/docs to "multiple complete command strings".

- **[D5] Per-operation `DriverContext` is mostly ignored by Docker CLI execution.** Docs say it carries per-operation host/certs/sudo/timeouts (`docs/index.md:131`; `DriverContext.cs:29-106`), but CLI execution uses component `Context` for global args/timeouts in `DockerCliDriverBase.Execution.cs:48-75`; method context is mainly diagnostic. Impact: direct `SysCtl<T>` callers cannot reliably override host/TLS/request timeout per call. Smallest fix: merge per-call context into execution or document it as diagnostic-only for CLI drivers.

### Minor

- **[D6] Build can return a service marked running after the container already exited.** `ContainerService.StartAsync` sets state to `Running` immediately after `docker start` (`ContainerService.cs:120-139`), and `WaitForContainerRunningAsync` can time out silently (`ContainerBuilder.WaitConditions.cs:340-353`). Impact: consumers may treat a dead container as healthy. Smallest fix: inspect after start and update state, or require/clarify wait conditions.

- **[D7] Docker Desktop daemon-switch cancellation can leave a child process running.** `ExecuteDockerCliCommandAsync` polls cancellation but does not kill the process in `DockerCliSystemDriver.Daemon.cs:93-158`. Impact: cancelled daemon recovery can keep mutating state after caller timeout. Smallest fix: wait with cancellation cleanup or kill in the cancellation path.

- **[D8] Daemon outage diagnostics are too thin.** `GetInfoAsync` drops stderr/exit context (`DockerCliSystemDriver.cs:38-44`), and `PingAsync` maps all CLI failures to "Docker daemon not reachable" (`DockerCliSystemDriver.cs:98-101`). Impact: on-call loses the real Docker error. Smallest fix: preserve `CreateErrorContext`, stderr, and exit code.

- **[D9] Docker CLI docs are not a production source of truth.** README duplicates quick starts (`README.md:64-126`, `README.md:289-395`), lifecycle hook examples are wrong (`docs/containers.md:326`, `339`), and sudo password docs describe an unsafe command-line prefix that implementation avoids (`docs/utilities.md:338-347` vs `DockerCliDriverBase.cs:156-168`). Smallest fix: one Docker CLI quickstart, aligned with tested behavior.

## Docker API driver

Examined: `FluentDocker/Drivers/Docker/Api/**`, Docker API kernel/builders, Docker API tests, README/docs references, and container flow from `WithinDockerApi` through `DockerApiDriverPack` to `DockerApiConnection`.

Verdict: **not production ready**. Happy paths exist, but private-registry auth, cancellation semantics, response disposal, copy-from behavior, and stream truncation handling are not safe enough.

### Critical

None found.

### Major

- **[A1] Registry auth is effectively non-functional for pull/push.** `DockerApiAuthDriver.LoginAsync` only posts `/auth` (`DockerApiAuthDriver.cs:18-25`), while `PullAsync`/`PushAsync` call image endpoints without an `X-Registry-Auth` header path (`DockerApiImageDriver.Build.cs:67-72`, `147-153`), and `PostStreamAsync` has no header API (`DockerApiConnection.cs:114-120`). Impact: private registry pull/push fails after "successful" login. Smallest fix: flow registry auth into image operations or stop advertising API login support.

- **[A2] Normal HTTP responses are not disposed.** Shared helpers create responses in `DockerApiDriverBase.cs:55`, `73`, `94`, `119`, `158`, `172`, `190`, `205`, and `225`; handlers read/return without owning disposal in `DockerApiDriverBase.ResponseHandling.cs:18-42`, `50-80`, `87-117`, and `120-129`; image remove repeats it in `DockerApiImageDriver.cs:136-148`, `177`. Impact: response/content resources are retained until GC under load. Smallest fix: make `HandleResponse*` own/dispose responses or wrap every caller in `using`.

- **[A3] Docker API service copy-from is broken for file destinations.** `ContainerService.CopyFromAsync` creates an existing temp file (`ContainerService.Operations.cs:89-93`), then API `CopyFromAsync` calls `Directory.CreateDirectory(hostPath)` (`DockerApiContainerDriver.Operations.cs:368-372`). Impact: file copy-from paths fail or become directories. Smallest fix: distinguish file vs directory destinations, or copy to a temp directory before reading the extracted file.

- **[A4] Caller cancellation is inconsistently swallowed.** Base error mapping propagates caller cancellation (`DockerApiDriverBase.ErrorHandling.cs:56-61`), but operation methods catch broad `Exception` without an `OperationCanceledException` guard (`DockerApiContainerDriver.Operations.cs:40-54`, `186-212`, `327-333`, `385-390`, `415-420`; `DockerApiStreamDriver.cs:258-276`; `DockerApiImageDriver.cs:138-145`). Impact: shutdown/cleanup can see cancellation as daemon failure. Smallest fix: rethrow caller cancellation before catch-all blocks.

- **[A5] Multiplexed stream truncation can look successful.** Exec demux appends partial payloads instead of failing when `payloadRead < frameSize` (`DockerApiContainerDriver.Operations.cs:253-265`), and log streaming silently ends on incomplete payload (`DockerApiStreamDriver.cs:358-361`). Impact: socket resets can return truncated stdout/stderr without error. Smallest fix: throw on incomplete frames after a valid header and add short-payload tests.

- **[A6] Docker API documentation is only a teaser.** README has a short Docker API example (`README.md:441-467`) and event stream sample without cancellation/bounds (`README.md:463-466`), but no auth limitations, private-registry headers, cancellation expectations, TLS guidance, or known unsupported semantics. Smallest fix: add a dedicated Docker API production doc linked from README.

### Minor

- **[A7] TLS hostname-mismatch option is unreachable through the public builder.** Config has `AllowTlsHostnameMismatch` (`DockerApiConnectionConfig.cs:42-46`), but `IDockerApiDriverBuilder.cs:11-50` exposes no equivalent. Impact: users may disable all TLS verification instead. Smallest fix: add a targeted builder option and flow it through `DriverContext`.

- **[A8] Tests miss production copy-from behavior.** Docker API integration copy-from only asserts success (`DockerApiDriverTests.Container.cs:347-351`), not file existence/content. Impact: the file-vs-directory bug survived. Smallest fix: assert extracted path/content and add service-level copy-from tests.

## Podman CLI driver

Examined: `FluentDocker/Drivers/Podman/**`, Podman kernel/builders, container/machine/pod/kube/manifest ports, Podman tests, README/docs, container flow through `PodmanCliDriverPack`, and machine/kube flow through auto-start and `IPodmanKubernetesDriver`.

Verdict: **not production ready**. Direct driver coverage exists, but the documented builder path, long-output operations, manifest existence handling, and machine auto-start are not production-grade.

### Critical

- **[P0] Podman builder container path can fail before invoking `podman`.** `ContainerBuilder.ExecuteAsync` nulls empty collections (`ContainerBuilder.cs:411-455`), while `PodmanCliContainerDriver.BuildCreateArgs` blindly enumerates collections (`PodmanCliContainerDriver.Args.cs:93-123`). The README uses this builder path (`README.md:97-106`, `330-339`). Impact: the documented happy path can throw a null-reference before the CLI runs. Smallest fix: make every collection enumeration null-safe and add a README-snippet-style Podman builder integration test.

### Major

- **[P1] Long-running "unbounded" operations are still stdout-capped at 4 MiB.** `ExecuteUnboundedCommandAsync` disables timeout only (`PodmanCliDriverBase.cs:177-178`) but still uses the 4 MiB buffered reader (`PodmanCliDriverBase.cs:103`, `243-247`; `PodmanCliDriverBase.Buffering.cs:31-36`). Callers include pull/push/build/save/load/import/kube/machine/exec/wait/cp/export. Impact: verbose production operations can fail despite succeeding externally. Smallest fix: stream or spool long-operation output with a rolling error tail and progress callbacks.

- **[P2] Manifest existence check masks outages as "not found".** `PodmanCliManifestDriver.ExistsAsync` returns `Ok(result.Success)` for every non-zero result (`PodmanCliManifestDriver.cs:255-259`). Impact: machine down, auth failure, or CLI execution failure is indistinguishable from missing manifest. Smallest fix: return `false` only for known not-found errors; otherwise return failure with context.

- **[P3] Machine auto-start create/start path is not production-grade.** Null `MachineName` is documented as default machine (`AutoStartMachineConfig.cs:10-13`), but auto-create uses literal `"default"` (`PodmanCliDriverPack.cs:393-397`); after start/init, it returns without health/ping wait (`PodmanCliDriverPack.cs:371-381`, `405-410`). Impact: can create an unexpected VM name and return before Podman is usable. Smallest fix: omit name when unspecified or use Podman's real default, then poll `PingAsync`/`info` until healthy or timed out.

- **[P4] Podman integration coverage misses the documented consumer path.** Tests use direct drivers in `PodmanDriverTestBase.cs:59-98`, while README promotes `Builder().WithinPodmanCli(...).UseContainer(...).BuildAsync()` (`README.md:97-106`, `333-339`). Impact: builder-path bugs escape direct-driver tests. Smallest fix: add an integration test copying the README container sample and a stopped-machine auto-start test.

### Minor

- **[P5] Streaming and global argument quoting is inconsistent.** Dynamic values are appended raw in `PodmanCliStreamDriver.cs:41-45`, `69-84`, `153-156`, and `PodmanCliDriverBase.cs:86-92`. Impact: time filters, socket paths, and detach keys with spaces split into invalid argv. Smallest fix: quote every dynamic CLI value.

- **[P6] Podman docs are duplicated and lack production caveats.** Podman quick starts appear in `README.md:64-142`, `README.md:289-374`, and `docs/index.md:97-129`; samples promote `WithAutoStartMachine()` without explaining macOS/Windows behavior, machine naming, readiness, long-operation cancellation, ignored progress, or stdout caps. Smallest fix: one "Podman production notes" section linked from samples.

## Model runner / OpenAI-compatible driver

Examined: `UseModelRunner().ForModel(...).BuildAsync()` through `ModelRunnerBuilder`, `ModelRunnerService`, `OpenAiModelInferenceDriver`, and `ModelApiConnection`; `ChatCompletionAsync`, `ChatStreamAsync`, Docker CLI model runtime/store drivers, endpoint/TLS config, env runner factory, model tests, `docs/model-runner*.md`, and README model sections.

Verdict: **not production ready**. The core path is substantial, but timeout classification, configure concurrency, endpoint truthfulness, SSE correctness, and docs drift are production-impacting.

### Critical

None found.

### Major

- **[MR1] HTTP timeouts are misclassified.** `ModelApiConnection.SendWithTimeoutAsync` throws `TimeoutException` on timeout (`ModelApiConnection.cs:330-332`), but `OpenAiModelInferenceDriver.PostJsonAsync` catches broad `Exception` and returns `RequestFailed` (`OpenAiModelInferenceDriver.cs:154-156`). Impact: callers cannot distinguish timeout from server failure. Smallest fix: propagate `TimeoutException` or map it to a dedicated timeout error.

- **[MR2] Configure races with pull/load/unload.** Build-time pull is gated (`ModelRunnerBuilder.cs:147-151`), but configure runs outside that gate (`ModelRunnerBuilder.cs:154-155`), and public `ConfigureAsync` is ungated (`ModelRunnerService.Engine.cs:62-66`). Impact: concurrent builders/services for one model can stomp persistent config while another load uses it. Smallest fix: include configure in `ModelOperationGate`.

- **[MR3] Reported endpoint can be wrong.** Docker CLI pack binds inference to `context.ModelRunnerEndpoint` (`DockerCliDriverPack.cs:97-100`, `236-244`), but `ModelRunnerBuilder` passes `ModelRunnerEndpoint.Default()` when no per-runner endpoint is supplied (`ModelRunnerBuilder.cs:139-140`), and runtime status reports `Default()` (`DockerCliModelRuntimeDriver.cs:162-166`). Impact: diagnostics/status can point to `localhost:12434` while inference uses another endpoint. Smallest fix: pass the pack/context endpoint through runner/status.

- **[MR4] SSE parsing is not full SSE.** Streaming treats each `data:` line as a complete JSON payload and ignores blank-line event delimiters (`OpenAiModelInferenceDriver.Streaming.cs:97-133`). Impact: standards-compliant SSE servers that split one JSON event across multiple `data:` lines fail as malformed. Smallest fix: accumulate `data:` lines until the blank delimiter, then parse the joined payload.

- **[MR5] Streaming idle timeout allocates per character.** `ReadBoundedLineAsync` creates a linked CTS/timer inside the character loop (`OpenAiModelInferenceDriver.Streaming.cs:145-186`). Impact: long streams can allocate thousands of timers/CTS instances. Smallest fix: read buffered chunks and apply one idle timeout per read operation.

- **[MR6] Env-created runners cannot use production TLS config.** `ModelRunnerEnvironment.CreateInferenceRunner` only accepts endpoint/model/apiKey and constructs `new ModelApiConnection(endpoint, apiKey: apiKey)` (`ModelRunnerEnvironment.cs:108-115`). Impact: env/Compose path cannot use private CA, mTLS, hostname mismatch, or timeout settings. Smallest fix: add overloads accepting `ModelApiConnectionConfig`.

- **[MR7] Streaming idle timeout docs contradict code.** Docs say `StreamReadIdleTimeout` null is the default and disables idle timeout (`docs/model-runner.md:449-451`), but code defaults to 120 seconds (`ModelApiConnectionConfig.cs:31-35`) and tests assert it (`ModelApiConnectionConfigTests.cs:30-37`). Impact: production streams can abort after 120s while docs say they wait indefinitely. Smallest fix: update docs to say default is 120s and null opts out.

### Minor

- **[MR8] Pull streaming failures lose stderr detail.** Progress streaming merges stdout/stderr, but non-zero exit reports only `exit code N` (`DockerCliDriverBase.Execution.cs:386-407`). Impact: failed `docker model pull` can hide useful registry/auth/network errors. Smallest fix: retain the last/truncated stderr/progress line.

- **[MR9] Env URL validation is weaker than `DOCKER_MODEL_RUNNER_URL`.** `ModelRunnerEnvironment.TryFromEnvironment` accepts any absolute URI before `Raw(...)` (`ModelRunnerEnvironment.cs:52-58`), while `ModelRunnerEndpoint.TryFromEnvironment` validates http(s)+host (`ModelRunnerEndpoint.cs:180-186`). Impact: invalid schemes can pass a `Try*` API and fail later. Smallest fix: reuse the same http(s)+host validation.

## Core public API / lifecycle / testing adapters

Examined: `Builder.UseContainer -> BuildAsync -> ContainerBuilder.ExecuteAsync -> ContainerService.DisposeAsync/BuildResults.DisposeAsync`, compose build/dispose, kernel build/dispose, xUnit/MSTest/NUnit lifecycle through `ResourceLifecycle`/`ResourceBase`, README quick starts, `docs/testing/**`, `docs/containers.md`, and `docs/compose.md`.

Verdict: **not production ready**. The core container path is coherent, but compose disposal can delete data, failed compose startup can leak resources, teardown timeouts are inconsistent, and several public lifecycle APIs are documented differently from their behavior.

### Critical

- **[C1] Compose dispose can delete volumes by default.** `ComposeService.DisposeAsync` calls `RemoveAsync(force: true)` (`ComposeService.cs:354-358`), and `RemoveAsync` maps `force` to `RemoveVolumes = _removeVolumes || force` (`ComposeService.cs:300-305`); Docker CLI turns that into `docker compose down --volumes` (`DockerCliComposeDriver.Args.cs:87-92`). Docs say volumes are only removed with `.WithRemoveVolumes()` (`docs/compose.md:501-514`). Impact: disposing a compose service/results can delete named-volume data. Smallest fix: decouple force from volume removal and add a regression test.

### Major

- **[C2] Failed compose startup leaks partial resources.** `InternalBuilders.cs:306-313` throws on failed `driver.UpAsync` and deletes only temp overlay files; it never runs `compose down`. Impact: failed pulls, health waits, or later-service failures can leave containers/networks/volumes in CI or production tests. Smallest fix: best-effort `DownAsync` with bounded cleanup when project/files are known.

- **[C3] Test adapter teardown timeout is not enforced.** `DockerResourceOptions.TeardownTimeout` promises hung-cleanup protection (`DockerResourceOptions.cs:96-100`), but `ResourceBase.DisposeAsync` creates a CTS (`ResourceBase.cs:217`) and directly awaits teardown/remove (`ResourceBase.cs:234`, `244`). Impact: if a driver ignores cancellation, xUnit/MSTest/NUnit cleanup can hang indefinitely. Smallest fix: wrap teardown tasks with `WaitAsync(cts.Token)`.

- **[C4] Non-container service disposal can hang on daemon outages.** `ContainerService` bounds cleanup with `WaitAsync` (`ContainerService.cs:332-358`), but `ComposeService`, `NetworkService`, `VolumeService`, and `PodService` directly await remove in dispose (`ComposeService.cs:354-358`, `NetworkService.cs:197-204`, `VolumeService.cs:147-154`, `PodService.cs:161-167`). Impact: public service disposal can block forever when Docker/Podman hangs. Smallest fix: use the same bounded cleanup pattern everywhere.

- **[C5] `ExecuteOnRunning` docs imply argv; implementation runs each string separately.** XML docs say "command and its arguments" (`IContainerBuilder.cs:441-448`), docs show `.ExecuteOnRunning("psql", "-U", ...)` (`docs/containers.md:313-327`), but implementation loops each string and calls `service.ExecuteAsync(command)` (`ContainerBuilder.WaitConditions.cs:143-148`). Impact: documented examples fail. Smallest fix: add argv support or make docs/API explicit that each string is a full command.

- **[C6] Documented lifecycle file features are not implemented as documented.** `CopyToOnStart` docs promise file or folder (`IContainerBuilder.cs:407-411`; `docs/containers.md:260-269`), but implementation uses `File.ReadAllBytes(hook.HostPath)` (`ContainerBuilder.WaitConditions.cs:139-142`). `ExportOnDispose(explode: true)` docs promise extraction (`IContainerBuilder.cs:419-437`; `docs/containers.md:302-310`), but implementation writes `hostPath + ".tar"` with no extraction (`ContainerService.cs:440-445`). Impact: users hit runtime failures or missing artifacts. Smallest fix: implement the documented behavior or remove it from docs/API.

- **[C7] `KernelBuilder.BuildAsync` leaks initialized drivers if a later registration fails.** It creates a kernel and registers configurations sequentially (`KernelBuilder.cs:84-105`) without catch/finally disposal. Impact: duplicate IDs, initialization failure, or cancellation can leave driver packs undisposed and unreachable. Smallest fix: dispose the partial kernel before rethrowing.

### Minor

- **[C8] `DestroyIfExists` ignores removal failure.** `ContainerBuilder.cs:401-404` awaits `driver.RemoveAsync` but never checks `response.Success` before creating the replacement. Impact: users get a misleading create/conflict failure instead of the cleanup failure. Smallest fix: check response and throw `DriverException`.

- **[C9] Container operation errors drop driver context.** `ContainerService.Operations.cs` throws `DriverException` with only `response.ErrorCode` in multiple methods such as `GetLogsAsync`, `ExecuteAsync`, copy/export/stats paths. Impact: callers lose diagnostics preserved elsewhere. Smallest fix: pass `response.ErrorContext` consistently.

- **[C10] HTTP wait polling does not dispose responses.** `ServiceExtensions.WaitForHttpAsync` and URL wait store `response` without disposing (`ServiceExtensions.cs:229-231`; `ContainerBuilder.WaitConditions.cs:303-313`). Impact: repeated waits can retain connections until GC. Smallest fix: `using var response = ...`.

- **[C11] Lifecycle docs are navigable but contradictory.** README/testing docs have indexes, but `docs/containers.md` contradicts implementation for command argv, directory copy, and export extraction. Impact: docs are easy to find but not production-reliable. Smallest fix: add executable snippet tests for lifecycle docs.

## Documentation / packaging / release readiness

Examined: `README.md`, `CHANGELOG.md`, `docs/**`, example READMEs, `.github/workflows/ci.yml`, `.github/workflows/pages.yml`, `Makefile`, `Directory.Build.props`, package `.csproj` files, package READMEs, and source APIs used to verify snippets.

Verdict: **not production ready**. Release automation, CI signaling, package READMEs, and first-contact docs can mislead consumers into unreleased or non-compiling paths.

### Critical

- **[R1] Stable release automation can publish an explicitly unreleased version.** `Directory.Build.props:6` sets `<Version>3.2.0</Version>`, `README.md:18` labels 3.2.0 "in development", and `README.md:270-273` says it is not on NuGet; `.github/workflows/ci.yml:175-203` publishes NuGet packages automatically on push to master/main/support. Impact: merging can publish preview APIs as a stable NuGet version. Smallest fix: release only from protected tags/manual approval and use `3.2.0-preview.*` until ready.

### Major

- **[R2] CI badge can imply runtime coverage that usually does not run.** `.github/workflows/ci.yml:11-16` disables scheduled integration tests; `.github/workflows/ci.yml:254-270` runs integration only by label/schedule/manual dispatch; `.github/workflows/ci.yml:311-320` skips when Docker is unavailable; DMR is label/manual self-hosted only at `.github/workflows/ci.yml:322-330`. Impact: green README CI mostly proves build/unit tests, not Docker/Podman/DMR behavior. Smallest fix: make releases depend on explicit integration/DMR gates or badge unit vs integration separately.

- **[R3] Copy-paste README snippets are not compile-ready.** `README.md:66-71` omits `FluentDocker.Services.Extensions`, then uses `ToHostExposedEndpoint` at `README.md:92-93`; the extension lives in `ServiceExtensions.cs:14`. Impact: primary onboarding fails in a clean project. Smallest fix: add required imports wherever extension methods are shown.

- **[R4] Async-first docs still teach blocking wrappers first.** README says v3 is async-first (`README.md:162`), but uses sync `Build()` at `README.md:170-175` and `docs/getting-started.md:71-81`; wrappers block on async in `Builder.cs:245-247`, `KernelBuilder.cs:78-80`, and `ServiceExtensions.cs:318-320`; sync dispose has reduced cleanup budget (`BuildResults.cs:19-23`, `150-160`). Impact: consumers can copy sync-over-async into ASP.NET/UI/test contexts. Smallest fix: make first-path docs use `await using` and `BuildAsync`.

- **[R5] Test/run docs overstate what integration runs.** `docs/test-categories.md:48-53` says `make test-integration` runs "ALL tests", but `Makefile:49-53` filters only `Category=Integration|Category=PodmanIntegration`; DevLocal/LongRunning/ManualOnly are separate/manual. Impact: release operators can believe they ran the full suite when they skipped known categories. Smallest fix: rename it to Docker/Podman integration subset and add a release-verification table.

- **[R6] NuGet package READMEs contain repository-relative links.** Package READMEs are packed (`FluentDocker.Testing.Xunit.csproj:12,21`), but `FluentDocker.Testing.Xunit/README.md:180-181`, `FluentDocker.Testing.MsTest/README.md:96-97`, and `FluentDocker.Testing.NUnit/README.md:142-143` link to `../docs/...`. Impact: NuGet users hit broken links. Smallest fix: use absolute GitHub Pages or repository URLs.

- **[R7] Onboarding leads with an unavailable preview feature.** README starts with "3.2.0 in development — Local LLMs" (`README.md:18-62`) while later saying the feature is not on NuGet (`README.md:270-273`). Impact: stable consumers are steered toward unavailable APIs. Smallest fix: move DMR below stable quick start or version-gate README docs.

### Minor

- **[R8] Docs are fragmented and duplicated enough to drift.** README is 600 lines; the docs site has a "New Here?" path (`docs/index.md:17-24`), but README links docs near the bottom (`README.md:590`) and repeats driver examples at `README.md:64-147` and `289-394`. Impact: no single obvious entry point. Smallest fix: shrink README to install, one async container, and "Start here" links.

- **[R9] Human migration docs expose an AI-agent skill as documentation.** `docs/migration.md:447` links a "Claude Code Migration Skill"; `docs/migrate-v2-to-v3/claude-skill.md:3`, `10-12` is tool-instruction content and hidden from nav/search (`docs/migrate-v2-to-v3/claude-skill.md:4-5`). Impact: production docs mix consumer migration guidance with agent prompt material. Smallest fix: move it out of public docs or into a separate automation section.

- **[R10] Generated API reference is produced but not discoverable.** Pages generates `docs/api-reference` (`.github/workflows/pages.yml:46-50`), and the template hides it from nav while keeping it searchable (`docs/_api-frontmatter.tpl:4-5`). Impact: raw API pages can appear in search without a curated landing page. Smallest fix: add an API reference landing link or exclude generated API pages from search until curated.
