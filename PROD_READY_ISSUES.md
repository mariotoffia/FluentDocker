# PROD_READY_ISSUES

Adversarial production-readiness review of FluentDocker (v3.2.0, `featrure/model-support`, HEAD `3b18aad`).
Nine independent adversarial subagent reviews, one per functional area / driver. Each chunk states what was
examined and lists findings ordered critical → minor. Questions evaluated per area:

1. Production ready? 2. .NET 10 best practices? 3. Documentation production ready? 4. Zero bugs?
5. Resilient (outages, recovery)? 6. Easy to consume/understand? 7. Docs findable/readable vs bloated/fragmented?

---

## Chunk 1 — Kernel & Driver Registry

**Examined:** `FluentDocker/Kernel/**` (FluentDockerKernel, KernelBuilder, DriverRegistry + Dispose/Helpers,
BuildScope, BuildResults, BuildFailureManifest, CapabilityChecks, driver builders), driver contracts
(`IDriver`, `IDriverPack`, `IDriverInterfaceResolver`, `DriverPackBase`), resolution exceptions, plus the
DockerCli/DockerApi/PodmanCli packs and 4,145 lines of kernel tests. All 170 kernel/registry unit tests pass
on net10.0. Findings verified by executing repros against the real assemblies.

**Verdicts:** Production ready: **NO** (one state-corruption bug + teardown-leak path). .NET 10 practices:
largely yes. XML docs: coverage yes, accuracy no. Zero bugs: no (1 critical, 3 major). Resilient: registration
rollback good; dispose-budget exhaustion silently abandons drivers. Consumable: yes — `Create()→WithX→BuildAsync→SysCtl<T>`
is discoverable and exceptions are actionable.

### Findings

**[CRITICAL] `Kernel/DriverRegistry.cs:115–121, 263–269` — registration failure path disposes instances the
registry never accepted, and can dispose instances it still serves.**
The catch is unconditional:

```csharp
catch
{
  if (reserved)
    await RollbackReservationAsync(driverId).ConfigureAwait(false);
  await DisposeDriverSafelyAsync(driver, _logger).ConfigureAwait(false);
  throw;
}
```

Every pre-acceptance failure — `DriverContextIdMismatchException` from `PrepareContext` (Helpers.cs:28–38),
duplicate-ID `DriverException` (Helpers.cs:40–46), even `OperationCanceledException` while waiting for the
lock — disposes the caller's instance. Verified repros:

- **A:** context-ID mismatch → retry with corrected context → `ObjectDisposedException`. The repo's own test
  `RegisterAsync_AfterContextDriverIdMismatch_RetryWithMatchingContextSucceeds` codifies retry-with-same-instance
  as supported; it only passes because its mocks don't enforce ODE.
- **B:** re-registering an already-registered instance under the same ID → duplicate rejected (`DRV_002`) **but
  the live registered pack is disposed while still registered** — `IsRegistered("api") == true`, every subsequent
  resolve throws `ObjectDisposedException`. A failed, recoverable operation corrupts healthy kernel state, silently.
- **C:** same instance under a second ID: pack throws "already initialized" → catch disposes it → the first,
  healthy registration is broken.

Contradicts the documented contract (IDriverRegistry.cs:31/84, FluentDockerKernel.cs:143/153: "On failure
**after acceptance begins**, the registry disposes the supplied instance"). Impact: one erroneous registration
call turns an unrelated healthy driver into an ODE-thrower for the kernel's life.
*Fix:* set `initStarted = true` immediately before `driver.InitializeAsync(...)`; in the catch dispose only
`if (initStarted)`. Fixes A/B/C while keeping failed-init teardown.

**[MAJOR] `Drivers/Docker/Cli/DockerCliDriverPack.cs:74–146` (same in `Podman/Cli/PodmanCliDriverPack.cs:57–117`)
— `InitializeAsync` has no double-init guard; immutability contract unenforced and inconsistent across packs.**
`DockerApiDriverPack` is hardened (`_initializeLock` + `if (_initialized) throw`), but the CLI packs set
`_initialized = true` with no check, no lock. Verified: registering the same `DockerCliDriverPack` under two IDs
silently succeeds, re-running init which mutates the plain `Dictionary<Type,object> _drivers` while the pack is
live and resolvable — violating IDriverPack.cs:13–14 ("must not mutate after initialization; resolution reads are
intentionally unlocked"). Unsynchronized `Dictionary` write concurrent with unlocked reads is UB (torn reads /
infinite loops), and aliased registration creates double ownership: unregistering `"a"` disposes the pack still
registered as `"b"`. The 132-finding pass hardened one pack and missed the other two.
*Fix:* copy the API pack's two-line guard into both CLI packs; optionally reject registering an instance already
registered under another ID.

**[MAJOR] `Kernel/BuildScope.cs:83–114` (amplified by `BuildResults.cs:131–146`) — cancelled/budget-exhausted
teardown removes services from tracking without disposing them; retry impossible.**
`DisposeAllAsync` snapshots **and clears** `_results` first, then bails on cancellation. `BuildResults.DisposeAsync`
shares one 60s CTS across **all** scopes and sets `_disposed = 1` up front — one hung daemon call in scope N
exhausts the budget, scopes N−1…0 enter with a cancelled token, clear their lists, dispose nothing, and a second
`DisposeAsync()` fast-path returns. For a library whose core promise is container cleanup, a slow daemon means
silently leaked containers/networks/volumes with no programmatic detection.
*Fix:* only remove a service from `_results` after its disposal completes (or re-add remainder on break); don't
latch `_disposed` until all scopes report empty.

**[MAJOR] `Kernel/BuildFailureManifest.cs` + `Builders/Builder.Cleanup.cs:47–50` — failure manifest discards
exception details; cleanup failures never logged.**
`catch (Exception ex) { kept.Add(ToResource(service, $"cleanup failed: {ex.Message}")); }` —
`BuildFailureResource` carries only a `Reason` string (no exception, error code, stack); no `_logger` call in
`CleanupFailedBuildAsync`, so a `DriverException` with `ErrorCode`/`IsTransient` is lost exactly when diagnostics
matter. Manifest is tucked into `ex.Data["BuildFailureManifest"]` — an untyped magic key.
*Fix:* log per-failure at Warning; add `Exception?`/error-code to `BuildFailureResource`; expose the `ex.Data`
key as a public const.

**[MINOR] `Kernel/DriverRegistry.Dispose.cs:58–68, 106–127` — per-driver budget exhaustion abandons drivers yet
declares disposal complete.** Skipped drivers are leaked with a warning while `IsDisposeComplete == true`; kernel
retry only fires on lock-timeout `TimeoutException`. Deliberate but undocumented/undetectable.
*Fix:* document abandonment in XML and/or surface an aggregate result.

**[MINOR] `Kernel/BuildResults.cs:17,27` — public ctor retains caller's `List<BuildScope>` by reference**
(`_scopes = scopes ?? []`); external mutation changes `All`/`Scopes`/disposal set. *Fix:* `[.. scopes]` copy.

**[MINOR] `Kernel/DriverRegistry.cs:171–196, 275–341` — null `driverId` on lookup/unregister surfaces raw
`ArgumentNullException` from `ConcurrentDictionary` internals** while `RegisterAsync` throws a curated
`ArgumentException`. Inconsistent contract; one `IsNullOrWhiteSpace` guard per entry point.

**[MINOR] `DockerCliDriverPack.cs:187–206`, `DockerApiDriverPack.cs:147–160` — `IsHealthyAsync` returns `false`
and `GetCapabilitiesAsync` succeeds after `DisposeAsync`**, but IDriver.cs:10 / IDriverPack.cs:16 mandate ODE
after disposal. Align docs or code.

**[MINOR] `Kernel/FluentDockerKernel.cs:375–388` — second concurrent `DisposeAsync()` can block up to ~120s**
(registration lock wait + retry) instead of returning promptly; deserves a doc note. Also `ISysCtl` omits
`ObjectDisposedException` from documented throws; `IKernelBuilder` doesn't state the builder is not thread-safe.

### What holds up

The concurrency core survived every attack: reserve→init-outside-lock→commit-with-recheck registration handles
dispose racing slow registration; the 0/1/2 dispose state machine is correct; concurrent `SysCtl` during
`DisposeAsync` degrades to clean ODE at every interleaving; lock ordering is consistent and deadlock-free;
default-driver fix-up on unregister is TOCTOU-safe; `CloneWith` deep-copies and `ToString` redacts sudo
passwords; the soft/hard exception taxonomy is precise. The 132-finding pass hardened read and dispose paths —
what it missed is **ownership on the failure paths**: who may dispose what, and when.

---
## Chunk 2 — Services Layer

**Examined:** `FluentDocker/Services/` + `Services/Impl/` + `Services/Extensions/` (excl. model services — see
Chunk on Model/DMR). All 35 files (~6,300 lines) read fully. Traced ContainerService create→start→stop→dispose
including every state assignment and failure branch; ComposeService up-with-bad-service; mapped the
ServiceRunningState machine. 674 service-layer unit tests pass on net10.0.

**Verdicts:** Production ready: **CONDITIONALLY** — container lifecycle solid; 2 MAJOR compose defects + HTTPS
wait defect to fix before ship. .NET 10 practices: largely yes (CT propagated, `ConfigureAwait(false)`
universal, events raised outside locks, Interlocked dispose). XML docs: interfaces genuinely good; impl gaps
(`CS1591` globally suppressed; 6 public ctors + 5 wait overloads undocumented). Zero bugs: no. Resilient:
mostly yes — `DriverException.IsTransient` is real, wait helpers retry on it, dispose time-boxed. Consumable:
yes — coherent interfaces, exceptions carry ErrorCode + ErrorContext + original error text.

### Findings

**[MAJOR] `Services/Impl/ComposeService.cs:88-92, 187-226` — `RefreshStateAsync`/`ListServicesAsync` never set
`ComposeListConfig.All`, so the documented `Stopped` outcome is unreachable on modern compose.**
`All` defaults `false`; CLI driver only appends `-a` when set (DockerCliComposeDriver.Args.cs:38-40). Compose
v2.21+ `ps` omits stopped containers by default → fully stopped project returns 0 rows →
`UpdateState(Unknown)`, while `IComposeService.RefreshStateAsync` docs promise "`Stopped` if all are
stopped/exited/dead". Unit tests mask it (mock returns stopped rows regardless of `All`). The `allStopped`
branch (214-220) is dead code against real Docker. `ConnectToExisting` + refresh on a stopped project reports
`Unknown`; orchestration keyed on `Stopped` never fires.
*Fix:* set `All = true` in refresh's list call; add a driver-level arg assertion test.

**[MAJOR] `Services/Impl/ComposeService.cs:228-380` + `ComposeService.Unpause.cs:11-42` — lifecycle ops lack
the `Removed` guard, breaking the engineered no-resurrection invariant.**
`RemoveAsync` (Remove.cs:20-23) guards double-remove; `RefreshStateAsync` comments "ps-empty must not resurrect
Removed". But Start/Stop/Pause/Unpause/Restart have no guard: after `RemoveAsync()`, `StopAsync()` runs
`UpdateState(Stopping)` → fails → `catch { UpdateState(Unknown); }` — state leaves `Removed`. Subsequent
`Dispose()` re-enters `RemoveAsync` (guard sees `Unknown`), re-running `compose down` and **re-firing
Removing/Removed hooks and events a second time**. ContainerService guards this exact case.
*Fix:* mirror ContainerService — throw (Start/Pause/Unpause/Restart) or early-return (Stop) when `Removed`.

**[MAJOR] `Services/Extensions/ServiceExtensions.cs:334-341` + `Common/SharedHttpClient.cs:15-22` —
`WaitForHttpAsync(useHttps: true)` can never succeed against self-signed TLS, and fails silently.**
Probe uses `SharedHttpClient.Instance`; the shared `SocketsHttpHandler` has no `SslOptions`/cert callback, so a
self-signed or hostname-mismatched cert (the norm for containerized HTTPS — and the probe dials an IP,
guaranteeing name mismatch) throws `HttpRequestException`, swallowed as "Not ready yet". Burns full timeout,
returns `false`. The `useHttps` feature is effectively nonfunctional for its primary use case.
*Fix:* dedicated probe client with permissive cert callback (readiness, not security), or document loudly and
surface the last exception on failure.

**[MAJOR] `Services/Impl/ContainerService.Operations.cs:145-184` + `ContainerService.Lifecycle.cs:444-464` —
container export buffered wholesale in memory; >2 GB throws, multi-GB risks OOM.**
`ExportCoreAsync` does `File.ReadAllBytesAsync(tempPath)` (IOException over int.MaxValue; LOH pressure below),
and the export hook doubles it: `byte[]` → `new MemoryStream(exportData)` → `TarFile.ExtractToDirectory` — when
the driver already wrote the tar to `tempPath` on disk. Export hooks on realistic images hard-fail or OOM at
dispose time.
*Fix:* stream from the temp file (`TarFile.ExtractToDirectory(File.OpenRead(tmp))`); consider a stream/path
based `ExportToFileAsync`.

**[MINOR] `ContainerService.Lifecycle.cs:106-135, 336-361` — abandoned stop task can flip state
`Removed → Unknown` and fire a stale event during dispose.** Abandoned `StopCoreAsync` later hits
`catch { UpdateState(Unknown); }`; `UpdateStateCore` only gates on `_disposeCompleted` (still 0). Subscribers
observe `Removed` then `Unknown`. *Fix:* freeze state (flag or version fence) before the remove phase.

**[MINOR] `ContainerService.cs:229-265` — `UnpauseAsync` is the only lifecycle op that doesn't transition to
`Unknown` on hard failure** — diverges from the file's own convention. *Fix:* align or document.

**[MINOR] `HostService.cs:18, 287-293` — `CanStart => true` while `StartAsync` is a documented no-op**,
contradicting the `IServiceCapabilities` contract that Image/Network/Volume follow (`CanStart => false`).
*Fix:* `CanStart => false`.

**[MINOR] `HostService.cs:271-280` — `CreateContainerAsync` doesn't seed `initialState`** (discovery path does);
fresh handle reads `Unknown` though daemon state is `created`. Also `ParseContainerState` duplicates
`ContainerService.ParseState` (drift hazard; both map `"created"` → `Starting`, mislabeling never-started
containers as in-flight). *Fix:* share one mapping helper; seed initial state.

**[MINOR] `HostService.Operations.cs:57-99` — `PullImageAsync("repo:1.2")` with default tag builds malformed
`repo:1.2:latest` inspect ref.** Digest refs are special-cased, tag-in-image is not; the ForcePull path splits
properly via `ParseImagePullReference` — inconsistent. *Fix:* parse when tag is default and image has a tag.

**[MINOR] `HostService.Operations.cs:175-176` — `CreateNetworkAsync` mutates the caller's `config`
(`config.Name = name`)**; reuse for two networks silently carries the last name. *Fix:* clone or assign when unset.

**[MINOR] `ContainerService.Lifecycle.cs:120-148` — `deleteNamedVolumeOnDispose` silently ignored when
`deleteOnDispose == false`** — keep-container + delete-volumes config leaks volumes without a log.
*Fix:* warn or honor independently.

**[MINOR] XML docs, impl surface** — undocumented publics: ctors of ComposeService/ImageService/NetworkService/
VolumeService/PodService/HostService; `pollIntervalMs` overloads (ServiceExtensions.cs:99,166,235,305;
ServiceExtensions.Logs.cs:36). `CS1591` suppressed solution-wide (csproj admits "non-Docker-API areas still
need a docs pass"). Ctor ownership semantics (`stopOnDispose`/`deleteOnDispose`/`disposeCleanupTimeout`) are
exactly what integrators need documented.

**[MINOR] `IEngineScope.cs:29-57` + `EngineScope.cs:171-211` — restore-on-dispose undocumented; failures
swallowed** (LogError only); when initial detection fails, restore is skipped entirely. Daemon can be left in
wrong OS mode with only a log line. *Fix:* document; expose restore failure.

**[MINOR] `ServiceEndpointResolver.cs:101-106` — DNS takes `addresses.FirstOrDefault()` unconditionally** —
may pick IPv6 for a host serving the port on IPv4 only; inconsistent with `EnvironmentExtensions`' deliberate
IPv4 preference. *Fix:* prefer IPv4 here too.

**[MINOR] `PodService.cs:114` — hardcoded 10s pod stop timeout**, not configurable. *Fix:* parameter.

**[MINOR] `EnvironmentExtensions.cs:248-254` — `GetDockerSocketPath()` ignores `DOCKER_HOST` and rootless
sockets** while `IsRootless()` directly below knows about `XDG_RUNTIME_DIR`. *Fix:* consult those first.

**[NOTE] Second concurrent `Dispose()` returns before the first completes** (CompareExchange idempotent, not
synchronized). Acceptable under the documented single-threaded lifecycle contract.

### What holds up

ContainerService state-machine discipline (single lock-guarded `UpdateStateCore`, change dedup, stale-inspect
write fence via cache versioning — genuinely well engineered); careful idempotency matchers anchored to
resource ids; error mapping drops nothing (`Error`, `ErrorCode`, `ErrorContext` propagate into every
exception); transient classification makes daemon-restart blips retryable and wait helpers honor it; dispose is
time-boxed, non-throwing, abandoned tasks exception-observed; wait helpers separate connect budget from poll
cadence and never confuse cancellation with timeout.

---
## Chunk 3 — Docker CLI Driver

**Examined:** `FluentDocker/Drivers/Docker/Cli/**` — DockerCliDriverBase partials (Execution, Streaming,
StreamSources, Unbounded, Context), Binary/, all non-Model component drivers (Container + partials, Image,
Compose, Network, Volume, System, Stream, Service, Stack, Auth), JsonLineParser, TimestampParser. Traced
create→run→inspect end-to-end through `ExecuteProcessAsync`/`ExecuteUnboundedProcessAsync` and streaming
iterators. Model drivers excluded (own chunk).

**Verdicts:** Production ready: **PARTIAL** — process-execution core genuinely hardened, but a culture bug
corrupts sentinel values on ICU-locale hosts and the error-code contract is broken for NotFound. .NET 10
practices: yes (ConfigureAwait consistent; cancel ⇒ kill tree ⇒ drain ⇒ dispose; no WaitForExit/ReadToEnd
deadlocks; readers before stdin). XML docs: PARTIAL (execution semantics unusually well documented; AttachResult
drain obligation missing). Zero bugs: no. Resilient: PARTIAL (daemon-connect → `Api.ConnectionFailed`,
timeout vs cancel distinguishable; but NotFound classification dead code). Consumable: yes, mostly.

### Findings

**[MAJOR] `Components/DockerCliContainerDriver.Operations.cs:284–301` (pattern repeats ~20×) —
culture-sensitive interpolation of numeric CLI args corrupts negative sentinel values.**

```csharp
args.Add($"--memory-swap {config.MemorySwap.Value}");   // Operations.cs:287
args.Add($"--cpu-quota {config.CpuQuota.Value}");       // Operations.cs:295
args.Add($"--pids-limit {config.PidsLimit.Value}");     // Operations.cs:301
```

Also `DockerCliContainerDriver.cs:110/147` (stop/restart `-t`), `Args.cs:33`, `DockerCliComposeDriver.Args.cs:78/95/106/121`,
`Info.cs:331/434`, `DockerCliServiceDriver.cs:40/121/338`, `DockerCliStreamDriver.cs:43`. Interpolation uses
`CurrentCulture`; on .NET 8/10 with ICU, cultures like `sv-SE`/`nb-NO` render the negative sign as U+2212 (−),
so `MemorySwap = -1` (docker's "unlimited" sentinel), `--cpu-quota -1`, `--pids-limit -1` serialize as `−1` —
docker rejects (`invalid argument "−1"`). Positive values unaffected — survives en-US CI, detonates on a
Swedish production host. The codebase knows the rule (`DockerCliImageDriver.cs:252–270` uses
`args.Append(CultureInfo.InvariantCulture, …)`) — it just isn't applied elsewhere.
*Fix:* `Value.ToString(CultureInfo.InvariantCulture)` at each site, or a shared `InvariantAdd` helper.

**[MAJOR] `Components/DockerCliContainerDriver.Inspection.cs:34–41` — `ErrorCodes.Container.NotFound` is
unreachable for a missing container; classification contract broken vs sibling drivers.**
`docker container inspect <missing>` exits 1 with stderr `Error: No such container: …` → control always takes
the `!result.Success` branch which maps to `InspectFailed` (CNT_007); the `NotFound` branch (:54–59) requires
exit 0 + empty JSON array, which the docker CLI never produces. Podman's driver DOES classify it
(`PodmanCliContainerDriver.cs:366–368`), and `DockerCliImageDriver` maps "No such image" for remove — so
driver-agnostic existence checks get `CNT_007` from Docker and `CNT_001` from Podman for the identical
situation. Same pattern in network/volume/image inspect.
*Fix:* map stderr "No such container" (case-insensitive) → `Container.NotFound` in the failure branch
(+ analogues for image/network/volume).

**[MINOR] `Components/DockerCliImageDriver.cs:217–220` — unguarded `File.Delete(iidFile)` in `finally`
converts a successful build into a failure.** Windows AV/indexer holding the temp iidfile throws; the computed
`Ok(ImageBuildResult)` is discarded and callers get `Fail(BuildFailed)` for an image that exists.
`Run.cs:84` wraps the identical cidfile delete in try/catch. *Fix:* copy that guard.

**[MINOR] `DockerCliDriverBase.Execution.cs:361` (also `Streaming.cs:36`, `StreamSources.cs:92`) — streaming
paths have no per-line length cap.** Buffered path capped at 64 MiB; unbounded keeps 256 KiB tail; but
`docker logs -f`/events/stats buffer an entire "line" before yielding — a container writing GBs with no `\n`
grows one managed string until host OOM. *Fix:* bounded line reader shared by the three pumps.

**[MINOR] `DockerCliDriverBase.Unbounded.cs:84–95` — generic catch reports `ExitCode = -1` even when the
process exited with a real code.** Reader fault after normal exit discards the true code; buffered path uses
`GetExitCodeOrDefault` for exactly this reason. *Fix:* same here.

**[MINOR] `DockerCliDriverBase.Execution.cs:226` + `DockerCliDriverBase.cs:264–267` — start-failure message
blames the docker binary when `sudo` failed to launch.** `StartInfo.FileName` is `"sudo"` under
`SudoMechanism.Password/NoPassword` but the error names the docker path. Misdirects triage. *Fix:* pass
`processFileName`.

**[MINOR] `DockerCliDriverBase.Context.cs:19–23` — per-op context overriding `Host` silently drops
component-level `VerifyTls` while inheriting `CertificatePath`** — TLS silently downgraded to unverified for
that call (bool default false; "unset" inexpressible). *Fix:* nullable `VerifyTls` in the merge; document rule.

**[MINOR] `Drivers/IStreamDriver.cs:343–470` (`AttachResult`) — XML docs omit the caller's drain obligation.**
Caller reading neither live stream blocks the child once the 64 KB pipe fills — the classic attach hang.
Disposal is correct; the obligation is just undocumented. *Fix:* one `<remarks>` paragraph.

### What holds up

Correct CommandLineToArgvW quoting incl. metachar/backslash handling + leading-`-` rejection for positional
args; readers-before-stdin deadlock avoidance; 64 MiB stdout throw-cap / 4 MiB stderr truncate-cap with
explicit markers; timeout-vs-caller-cancel disambiguation (GEN_003 vs rethrown OCE); cancellation ⇒ tree kill ⇒
drain ⇒ dispose on every path; `TryComplete` before kill in the merged streaming pump; sudo password only via
stdin; `--password-stdin` for login; race-free ID capture via cidfile/iidfile; culture-invariant *parsing*
throughout; JSON-line parser refuses all-failed parses; timestamp parser handles Go reference formats; pack
lifecycle sound. Substantial test coverage of exactly these seams.

---
## Chunk 4 — Model / DMR Subsystem

**Examined:** ~8,400 lines — model ports (`IModelInferenceDriver`, `IModelManagementDriver`,
`IModelRuntimeDriver`, `IModelBackendInfo`), `DockerCliModel*` drivers, `Drivers/Models/**`
(OpenAiModelInferenceDriver + Streaming, ModelApiConnection), model services (`ModelRunnerService` + partials,
`ModelService`, `GenericOpenAiModelRunner`, `IModelRunner`/`IModelService`/`IModelEngine`/`IModelInference`/
`IModelStore`), model builders, `ModelEnvName`/`ModelOperationGate`/`ModelRunnerException`, `ModelReference`/
`ModelRunnerEndpoint` value objects. Build clean; all 298 model-subsystem unit tests pass (net10.0 Release).

**Verdicts:** Production ready: **YES** — no data-corruption, deadlock, or leak paths found. .NET 10 practices:
yes (IAsyncEnumerable + EnumeratorCancellation, cancellation honored mid-stream, verified no response leak on
early `break`, correct SemaphoreSlim gate). XML docs: yes — genuinely production grade. Zero bugs: no (1 MAJOR,
6 MINOR). Resilient: PARTIAL — detection/classification excellent, but **zero built-in retry** (documented as
deliberate; callers own retry policy). Consumable: yes — store/engine/inference split coherent, failure modes
typed and discoverable.

### Findings

**[MAJOR] `Services/Impl/ModelService.cs:173-177` — Dispose during in-flight shared load publishes *success*
to every waiting `StartAsync` caller.**

```csharp
catch (OperationCanceledException) when (Volatile.Read(ref _loadCancellationSignaled) != 0)
{
  loadCts.Cancel();
  Volatile.Write(ref _loadInitiated, 0);
  completion.TrySetResult(true);
}
```

When `DisposeAsync` cancels mid-load, the shared-load driver resolves the TCS with `true` — a concurrent
`StartAsync` waiter completes **successfully** though the model was never loaded and the service is disposed
(`State == Removed`). The caller's next call fails with `ObjectDisposedException` instead of a truthful signal
at the await site. Intentional and asserted by a test — but a success signal for a load that did not happen is
a semantic trap in teardown races. Code gating on "StartAsync OK → model is up" proceeds on a false premise.
*Fix:* `TrySetException(new ObjectDisposedException(...))` (or `TrySetCanceled`) in this arm; update the test.

**[MINOR] `ModelService.cs:249-252` — post-load cancel/dispose race returns success with `State == Starting`**
(early return skips `UpdateState(Running)`; TCS still set true; Running hooks never fire). *Fix:* cooperative
`ThrowIfCancellationRequested` so existing catch arms classify it.

**[MINOR] `ModelService.cs:154` — fire-and-forget shared load can raise `UnobservedTaskException`** if all
waiters abandon and the load then faults. *Fix:* observe the TCS exception (OnlyOnFaulted continuation or
log-and-observe in `FaultSharedLoad`).

**[MINOR] `Drivers/Models/Connection/ModelApiConnection.Streaming.cs:91-104` — `RequestOwningStream` omits the
`Task`-based `ReadAsync(byte[],int,int,CT)` override that `ResponseOwningStream` deliberately provides.**
External consumers of public `PostStreamAsync` calling the classic array overload fall through to
`Stream.BeginRead/EndRead` → synchronous `Read` on a pool thread — pending SSE read becomes uncancellable.
*Fix:* one-line override delegating to the `Memory<byte>` overload.

**[MINOR] `ModelRunnerService.Store.cs:87` — `PurgeAllAsync` acquires no `ModelOperationGate` while every
per-model mutation does** (same hole in `UnloadAllAsync`, Engine.cs:67). Purge-all can delete a model mid
gated pull — the exact interleaving the gate prevents. *Fix:* document the exemption (all-gate likely overkill),
matching the `TagAsync` accepted-race precedent.

**[MINOR] `ModelRunnerService.Engine.cs:38` (+ Store.cs:14,46,70; Engine.cs:75) — missing
`ArgumentNullException.ThrowIfNull(model)` where sibling methods guard.** Null model silently acquires the `""`
gate then dies as NRE inside the driver. *Fix:* one-line guards.

**[MINOR] `DockerCliModelRuntimeDriver.cs:266-277` — `Detach = false` spawns an interactive `docker model run`
chat session nothing consumes, executed `unbounded: true`** — caller can wait on their own token indefinitely.
*Fix:* reject or warn on `Detach == false` in `LoadAsync`.

### What holds up

SSE framing survives multi-line data, `[DONE]` + whitespace, CRLF/LF/lone-CR across chunk boundaries, UTF-8
splits (stateful `Decoder`, U+FFFD flush), BOM, comments, 1M-char bombs, mid-stream error frames — with a
fixture-driven suite proving it. Disposal on early `break` is leak-free through the
`RequestOwningStream → ResponseOwningStream → response+request` chain. Cancel vs first-byte vs idle timeout are
cleanly disambiguated into typed codes. `ModelOperationGate` correct (no deadlock/convoy; builder re-entrancy
verified safe). `ModelReference` validation/equality matches Docker's heuristics. Security (API-key-over
plaintext guard, correct quoting, env-name injection regex, CA pinning) and XML docs are of a quality rarely
seen — including honest documentation of what the library deliberately does NOT do (retry).

---
## Chunk 5 — Common / Model / Extensions (shared core)

**Examined:** all `FluentDocker/Common/**` (excl. model-specific) + `FluentDocker/Extensions/**` in full
(3,372 LOC); `FluentDocker/Model/**` spot-checked on the riskiest (ModelReference + Equality, CommandResponse,
ErrorContext, ErrorCodes, DriverContext, TemplateString, EmbeddedUri, DockerUri, HostIpEndpoint,
ContainerBuildParams, Unit). `CommandLineQuoting` verified **empirically**: 35 adversarial arguments (empty,
trailing `\`, embedded/nested quotes, newlines, unicode, `$`/backtick/globs) round-tripped through a real child
process — all OK. 313 `CoreTests.Common` tests pass. SharpCompress 0.48.0 binary confirmed to contain the
zip-slip guard.

**Verdicts:** Production ready: conditionally yes — zero criticals; injection barrier and JSON layer solid;
3 majors to fix before tagging. .NET 10 practices: largely yes (SearchValues, GeneratedRegex, cached read-only
serializer options — zero per-call allocations found, invariant culture throughout, equality contracts all
correct). XML docs: **NO** — Extensions has ~15 bare public members and `CS1591` is suppressed so gaps are
invisible to CI. Zero bugs: no. Resilient: mostly — truncation keeps the tail so fatal lines survive; but
context metadata never prints. Consumable: yes — coherent exception hierarchy, legacy monads properly obsoleted.

### Findings

**[MAJOR] `Common/ErrorContextExtensions.cs:266–267` — `Map` silently discards `Output` on the failure path.**
Both `Fail` overloads have a trailing `string? output = null` never passed, while the success path forwards it.
Any pipeline that maps a failed response before logging loses the captured command output — exactly the
3am-diagnosis data the hardening pass meant to preserve. *Fix:* append `output: response.Output` to both calls.

**[MAJOR] `Common/ImagePullException.cs:23,32,41` — every pull failure is hard-coded `isTransient: true`.**
"manifest unknown", "unauthorized", "denied: requested access" will never succeed on retry — a retry policy
keyed on `DriverException.IsTransient` (the documented retry signal) hammers the registry and hides permanent
misconfiguration. *Fix:* `isTransient` ctor param; classify not-found/auth as non-transient at throw sites.

**[MAJOR] `Extensions/ModelExtensions.cs:42–78` + `Model/Containers/ContainerBuildParams.cs:179–237` — public
unquoted command-line builder bypasses the injection barrier.** `sb.OptionIfExists("--build-arg ",
BuildArguments)` etc. — no `CommandLineQuoting`, no validation; a label value of `x --privileged` or a path
with spaces produces an argument-injecting command line. `ContainerBuildParams.ToString()` has zero internal
consumers (dead public surface) but remains a shipped API external callers will feed to a CLI. Sibling legacy
helpers got `[Obsolete]` in the hardening pass; this one was missed.
*Fix:* quote through `QuoteArgumentIfNeeded` or `[Obsolete]` the surface like the rest.

**[MINOR] `Model/Drivers/ErrorContext.cs:90–110` — `ToString()` omits `Metadata`, `StdErr`, `StdOut`.**
`ForContainer` stores the container id in `Metadata["containerId"]` which no ToString renders;
`DriverException.ToString()` uses it. Default production logging shows *which operation* failed but not *on
which container/image/network*. *Fix:* append Metadata (and truncated StdErr) to ToString.

**[MINOR] `Common/LenientInt32Converter.cs:19–20,26–27` — out-of-range/fractional numbers silently become 0.**
`4294967296` or `3.5` reads as `0` with no error channel; applied to `BridgeNetwork` int fields; overflow-to
zero indistinguishable from genuine 0. *Fix:* clamp on overflow or fall through `double` for integral values.

**[MINOR] `Common/DriverContextIdMismatchException.cs:10` — sole exception outside the `FluentDockerException`
umbrella** (`: ArgumentException`); `catch (FluentDockerException)` misses it. *Fix:* document or re-parent.

**[MINOR] `Common/JsonHelper.cs:204,220,236` + `TolerantStringConverter.cs:41–44` — lenient string coercion is
global, not field-scoped.** Every `string` property in every model now silently accepts numbers/booleans —
schema drift the hardening pass would surface as `JsonException` is permanently masked library-wide; remarks
justify only three inspect fields. Also `ValueSpan.ToArray()` allocates needlessly.
*Fix:* `[JsonConverter]` on the known drifting fields; span-based `GetString`.

**[MINOR] XML doc gaps (invisible — `CS1591` in NoWarn, FluentDocker.csproj:20).** Missing entirely:
`CompressionExtensions`, `CliOutputTruncation` (+ members), `FileExtensions.ToFile/FromFile/CopyTo` + both
`EscapePath` overloads, `ComparisonExtensions`, `EnvironmentExtensions`, `ModelExtensions` (10 of 13 members).
Misleading: `StringExtensions.WrapWithChar` takes a `string`; `RequestResponse.Code` undocumented 0-when-error.
*Fix:* document; drop `CS1591` from NoWarn for the FluentDocker project.

**[MINOR] `Model/Common/TemplateString.cs:110–123` — `${E_NAME}` matching case-sensitive and O(all env vars).**
On Windows `${E_Path}` misses `PATH`; every render enumerates the environment. *Fix:* regex-extract tokens,
look up via `GetEnvironmentVariable`.

**[MINOR] `Extensions/EnvironmentExtensions.cs:32–45` — env `name` barely validated** (`MY VAR=x` flows into a
silently broken Dockerfile `ENV` line while values get full treatment). *Fix:* reject whitespace/quote names.

**[MINOR] `Common/CliOutputParser.cs:80` — `(long)(num * multiplier)` overflow yields `long.MinValue`**
(negative spike) instead of a clamp for hostile/corrupt sizes. *Fix:* saturate at `long.MaxValue`.

**[MINOR] `Common/DirectoryHelper.cs:38–42` — process-wide mutable public `GetTempPath` hook** — documented
startup-only test seam, but a global any assembly can redirect (`${TMP}` flows through it).
*Fix (optional):* internal setter.

### What holds up

`CommandLineQuoting` is a correct CommandLineToArgvW implementation, proven by live round-trip; its docs
correctly ban shell routing. `ShellArgParser` sane for its single consumer. No zip-slip (SharpCompress guard
verified in shipped binary; method `[Obsolete]`-fenced anyway). `JsonHelper` exemplary (cached options,
TryDeserialize overloads that can surface the swallowed exception). Truncation keeps the tail. Equality
contracts uniformly correct incl. case-insensitive registry hashing in `ModelReference` and digit-by-digit
port/digest validation. The hardening pass's obsoleting choices were deliberate and compat-preserving — the
misses above are omissions, not misjudgments.

---
## Chunk 6 — Documentation & Testing Support Packages

**Examined:** README.md, FluentDocker/README.md (NuGet), CHANGELOG.md, DEVELOPMENT.md, full `docs/**` tree
(incl. migrate-v2-to-v3, testing/*, adr/), Examples/** (built `Examples.sln` — 0 errors), and the shipped
testing packages: `FluentDocker/Testing/Core/**`, `FluentDocker.Testing.Xunit/**`, `.NUnit/**`, `.MsTest/**`.
Automated cross-check of all 307 method tokens in doc code fences against source; link check repo-wide; NuGet
published versions verified (3.1.0 latest stable; repo is 3.2.0-preview.2).

**Verdicts:** Docs production ready: conditionally yes — 0 stale API references in samples (exceptional), 0
broken in-repo links; blockers are 2 site-root-escaping links (404 on published site), a 20×-duplicated preview
banner with a typo'd branch name, and README omitting the most dangerous breaking change. Testing packages
.NET 10: largely yes (right idioms per framework). Whole-doc coverage: strong, one real gap
(`FLUENTDOCKER_TEST_SESSION` absent from the site). Zero bugs: **no — 1 CRITICAL ownership bug**. Resilience
documented: yes — troubleshooting.md is a real 3am document. Consumable from docs alone: yes — one obvious
path. Findable/readable: well-structured, not bloated; bloat limited to banner duplication + drifting triple
quick-start.

### Findings — Documentation

**[MAJOR] `README.md` §"Breaking changes 3.1.0 → 3.2.0" — omits the `WithPort(host, container)` parameter-order
flip**, the single most dangerous break (silent semantic swap that still compiles; CHANGELOG lists it *first*).
*Fix:* one bullet with before/after.

**[MAJOR] `docs/troubleshooting.md:92`, `docs/advanced-drivers.md:24` — links `../CHANGELOG.md` / `../README.md`
escape the Jekyll site root** (site builds from `docs/` only) → 404 on the published GitHub Pages site (masked
when browsing github.com). *Fix:* absolute GitHub URLs.

**[MAJOR] ~20 files — preview banner duplicated 20× verbatim, each hardcoding the typo'd branch
`featrure/model-support`**; two pages already display `feature/...` while linking `featrure/...` — copies are
drifting. DEVELOPMENT.md's GA checklist mandates manually sweeping exactly these. *Fix:* single Jekyll include;
optionally rename the branch.

**[MAJOR] docs site gap — `FLUENTDOCKER_TEST_SESSION` documented only in the 3 adapter READMEs, nowhere in
`docs/`.** It is the core mechanism preventing orphan cleanup from eating a sibling CI job's resources — the
primary mitigation for the OrphanCleanup finding below is invisible on the site. *Fix:* "Session isolation"
subsection in docs/testing/core.md.

**[MINOR] `docs/testing/core.md` vs `docs/testing/xunit.md:67` (and nunit.md) — contradiction on orphan
cleanup**: core.md "never removes running containers" (true) vs adapters "can delete a parallel job's live
containers". Accurate for networks/volumes (no in-use check); the imprecision hides the real risk.
*Fix:* reword to "stopped containers, and networks/volumes even if in use".

**[MINOR] `FluentDocker/README.md:47` (NuGet shop window) — Quick Start uses sync `ToHostExposedEndpoint`**
while root README + docs are async-first. *Fix:* align.

**[MINOR] `Examples/Simple/Program.cs` — flagship example pins `postgres:9.6-alpine` (EOL 2021).**
*Fix:* `postgres:16-alpine`.

**[MINOR] `docs/getting-started.md:43` — "featrure" typo inside a sample's XML comment.**

### Findings — Testing packages

**[CRITICAL] `Testing/Core/ComposeResource.cs` (TeardownAsync ~141-150) — a `ComposeResource` built with the
public `ConnectToExisting()` API stops and `compose down`s the borrowed project at fixture dispose.**
Evidence chain: `InternalBuilders.cs:307/332-339` sets `BorrowedProject=true, downOnDispose:false`; but
`downOnDispose` only guards `ComposeService.DisposeAsync` — `ComposeService.Remove.cs:16-52` runs
`driver.DownAsync` with **no borrowed check**; `ComposeResource.TeardownAsync` calls `StopAsync()` +
`RemoveAsync()` unconditionally. The same class guards borrowed projects in `RemoveStaleComposeAsync` (96-101),
proving the intent and the inconsistency. Directly contradicts docs/compose.md:515-516 ("the returned compose
service is borrowed: dispose... never runs `docker compose down`"). Attach a fixture to your long-running
dev/CI stack — the entire point of `ConnectToExisting` — and the first test run **deletes it**.
*Fix:* capture borrowed-ness in the resource at provision; when borrowed, Teardown/ForceRemove release local
resources only (mirror the stale-compose guard).

**[MAJOR] `Testing/Core/OrphanCleanup.cs` — `CleanupOrphansOnInit` defaults true, matching the global
`fluentdocker.managed=true` label**: any user on a shared daemon deletes *other* sessions' stopped containers
>1h and networks/volumes >1h — networks/volumes with **no in-use check** (315-318, 360-367; the running
container guard protects containers only). Overnight debugging session in another checkout = valid target.
Mitigations exist (age fail-safe, session env var) but the main one is undocumented on the site.
*Fix:* skip networks with attached containers; consider scoping or defaulting off outside CI.

**[MAJOR] `Testing/Core/ProcessExitReaper.cs:175-179, 96-103` — `Registration` holds a strong kernel
reference, so `TryGetKernel` can never return false**; the "kernel collected" branch is unreachable dead code —
the shape of a broken `WeakReference` refactor. Disposed kernels kept alive for process lifetime; exit cleanup
runs against disposed kernels with failures swallowed. *Fix:* restore `WeakReference<>` or remove the dead
branch and document the strong-ref lifetime.

**[MAJOR] `Testing/Core/ProcessExitReaper.cs:221-226` — `OnPosixSignal` sets `context.Cancel = true` then
`Environment.Exit(130/143)`**: a *library* forcibly terminating the process from a signal handler can preempt
the test host's own SIGINT/SIGTERM handling (result flushing, TRX writing). Opt-in env var bounds blast radius.
*Fix:* run cleanup but leave termination to the host, or document the takeover loudly.

**[MINOR] `ResourceBase.cs:175 vs 370` — reaper `Register` on every init attempt, `Unregister` only on fully
successful dispose** — failed-init retries inflate registrations; dead registrations linger to process exit.

**[MINOR] `XunitContainerFixtureBase.cs:93-98` — `IsDockerAvailableAsync` builds+disposes a fresh kernel per
call, no memoization** — post-`SkipWhenUnavailable`-removal pattern calls it per test body. docs say "cache in
your fixture"; the shipped fixture doesn't. *Fix:* `Lazy<Task<bool>>`.

**[MINOR] `XunitConditionalContainerFixtureBase.cs:69` — pre-init health probe passes `CancellationToken.None`
with no timeout wrapper** — stalled-but-accepting daemon socket hangs fixture init indefinitely.
*Fix:* linked CTS with short timeout.

**[MINOR] `OrphanCleanup.cs:399-431` — abandoned-late-provision marker consumed before `RemoveAsync`**; if
removal fails the marker is gone and later sweeps won't retry (resource survives until the age sweep).

**[MINOR] `ComposeResource.cs:~254` — session-label overlay written under `CurrentDirectory/.out/...` rather
than OS temp** — SIGKILL leaks it into the project tree.

**[MINOR] MsTest package gap — no `[AssemblyInitialize]` convenience/recipe** for suite-wide shared resources
(xUnit gets collection fixtures; MSTest story missing).

### Bloat / delete list

- 20× duplicated preview banner → one Jekyll include (fix `featrure` target while at it).
- `docs/index.md` "What's New in v3.0.0" — two-releases-old marketing on the front page → one CHANGELOG link.
- Triple quick-start drift (root README vs NuGet README vs getting-started) — keep (different audiences) but
  they've already diverged (sync vs async); declare the NuGet readme canonical-minimal and align.
- `postgres:9.6-alpine` in Examples/Simple — modernize.
- Nothing else: no dead references, no orphaned pages, model-runner 3-file split is genuine hierarchy (Jekyll
  parent/child nav), api-reference.md is a real pointer to CI-generated xmldocmd output, migrate-v2-to-v3 is
  current.

### What holds up

API accuracy of doc samples is exceptional (307/307 tokens current); link hygiene near-perfect; Examples build
clean; CHANGELOG honest (every spot-checked claim verified in source); troubleshooting.md maps real error
codes to causes; ResourceBase lifecycle carefully engineered (provision-generation commit protocol, teardown→
force-remove fallback, correct xUnit failed-init handling); adapters use the right idioms per framework; ADR
0001 candidly records the packaging debt.

---
## Chunk 7 — Docker API Driver

**Examined:** `FluentDocker/Drivers/Docker/Api/**` (Connection incl. unix socket/npipe/tcp+TLS, all Components
incl. Container partials, Image Build/BuildContext/SaveAtomic, Stream demux, TarWriter, DockerIgnoreFilter,
RegistryAuth) + `Drivers/Connection/ResponseOwningStream.cs` (~9,000 lines, all files read). Build clean; all
575 `DockerApi*` unit tests pass. Findings verified against Docker's stdcopy/ustar/patternmatcher protocols and
moby daemon source (`daemon/pkg/registry/auth.go`).

**Verdicts:** Production ready: **PARTIAL** — connection layer, demux, tar core, disposal ownership genuinely
solid; but registry-authenticated build is functionally dead, an opt-in timeout corrupts caller buffers, and
build contexts silently mangle symlinked dirs. .NET 10: mostly yes (shared handler, pooled unix/npipe
ConnectCallback, ResponseHeadersRead streaming, systematic CT + ConfigureAwait). XML docs: **NO** — systemic
`<inheritdoc/>`-on-nothing renders empty IntelliSense across ~6 files. Zero bugs: no. Resilient: PARTIAL
(classification real; pull can declare false success). Consumable: mostly yes.

### Findings

**[MAJOR] `Components/DockerApiRegistryAuth.cs:66-80` + `Drivers/IAuthDriver.cs:55-56` — `X-Registry-Config`
(build auth) can never carry credentials; Docker Hub keys can never match.**
`ToBase64Url(JsonHelper.Serialize(configs))` serializes `RegistryLoginConfig` whose `Password` is
`[JsonIgnore]`, and `Server` doesn't map to the daemon's `serveraddress`. Independently, keys are normalized to
`"docker.io"` while moby's `resolveAuthConfig` looks up `https://index.docker.io/v1/` for the official index
and never reaches the hostname-fallback for it. So `LoginAsync` + `BuildAsync` with a private base image fails
auth against **every** registry — the header contains a username and no password. Login→build is dead on
arrival; only pull/push/service-create work (their `HeaderFor` builds JSON manually with the password).
*Fix:* build the map with the same anonymous-object shape as `HeaderFor` (username/password/email/
serveraddress); key Docker Hub as `https://index.docker.io/v1/`.

**[MAJOR] `Connection/DockerApiConnection.StreamIdleTimeout.cs:50-61` — abandoned read after idle timeout
writes into a caller buffer that has been given back.**
`Task.WhenAny(readTask, delayTask)` → on timeout, throws while `readTask` still holds the caller's
`Memory<byte>`. With `StreamIdleTimeoutTicks` enabled, `ArrayPool`-rented buffers can be handed to unrelated
code before the late read scribbles daemon bytes into them — classic use-after-return corruption, plus an
unobserved-task exception risk. Opt-in today, which is why it's not CRITICAL.
*Fix:* linked CTS, cancel on timeout, `await readTask` (swallow OCE) before throwing — never leave a read in
flight against a caller buffer.

**[MAJOR] `Components/DockerApiImageDriver.BuildContext.cs:174-177` — in-context directory symlinks vanish
from the build context entirely.**
A dir symlink is neither traversed nor emitted as a symlink entry (only `FileInfo` reparse points reach
`WriteSymlinkAsync`). `COPY symlinked-dir /app` fails "not found" though CLI `docker build` works — silent
divergence from Docker on real monorepos (pnpm, Bazel outputs).
*Fix:* write a type-'2' symlink entry for in-context dir symlinks (same containment check as file symlinks).

**[MAJOR] `Components/DockerApiTarWriter.cs:45-46` + `BuildContext.cs:84` — symlink target >100 UTF-8 bytes
aborts the whole build with the wrong exception type.**
Throws `InvalidOperationException`, which the best-effort loop's `when (IOException or
UnauthorizedAccessException)` filter doesn't catch — one deep relative symlink target (routine in pnpm trees)
fails the entire `BuildAsync` with a raw exception instead of `Fail`, while entry *names* >100 bytes get a
proper GNU `@LongLink` 'L' fallback. *Fix:* emit a GNU 'K' long-linkname block (mirror of 'L' logic).

**[MINOR] `DockerApiDriverBase.Stream.cs:196,204` / `Demux.cs:75` / `Operations.cs:303` — stdcopy `Systemerr`
(frame type 3) rejected as protocol corruption.** Daemon-side mid-stream error text becomes a generic "invalid
frame header" failure, discarding the diagnosis. *Fix:* accept type 3, route payload to the error channel.

**[MINOR] `Components/DockerApiImageDriver.Build.cs:96-158` — pull success requires only "some progress lines
and no error line".** No terminal-status check, no post-pull existence probe (Build requires `aux.ID`, Load
requires "Loaded image"). Clean chunked-stream termination mid-pull yields false `Ok`; next create fails with
confusing "no such image". *Fix:* require terminal status or probe `GET /images/{ref}/json` after EOF.

**[MINOR] `Connection/DockerApiConnection.cs:380-398` + `.Tcp.cs:89-97` — stale `DOCKER_CERT_PATH` alone
force-upgrades `tcp://` to TLS then throws** when the dir is missing (CLI honors cert path only when TLS
enabled). Fail-closed but a support-ticket generator. *Fix:* require `DOCKER_TLS_VERIFY` or explicit https.

**[MINOR] `Components/DockerApiContainerDriver.ExecInspect.cs:15-27` — exec exit-code poll capped at 5×100 ms**
then fails "not available yet" — on a loaded daemon a command that *succeeded* is reported failed; CI flake
source. *Fix:* backoff until token/config deadline.

**[MINOR] API-surface consistency nits:**
- `PullAsync`/`RenameAsync` etc.: null image → raw `ArgumentNullException` instead of `Fail(InvalidArgument)` —
  inconsistent with the envelope contract everywhere else.
- `CopyFrom.cs:104-122`: archive symlinks skipped with a warning (docker cp preserves them); extracted files
  never get `File.SetUnixFileMode` — binaries lose `+x` on round-trip.
- `DockerApiTarWriter.cs:210-220`: entries >8 GiB throw (no base-256 encoding) — acceptable, but should
  surface as `Fail`, not an exception escaping `BuildAsync`.

**[MINOR] Systemic `<inheritdoc />` on members that inherit nothing → empty IntelliSense.**
Spot-verified: `DockerApiTarWriter.cs:16,28,38,52,59,76` (static methods), `DockerApiRegistryAuth.cs:16-65` +
`AuthCache`, `DockerApiDriverBase.ErrorHandling.cs` (6 sites), `.Headers.cs:13,31`, `TailBytes`
(`Logs.cs:126,154`), `PrefixReadStream`, `RegistryLoginConfig.ToString()`. The marker silently resolves to
nothing — for a "production hardened" pass this is the largest doc regression. *Fix:* real `<summary>` each;
enforce via DocFX warnings-as-errors.

**[MINOR] Comments contradicting code:** `DockerApiDriverBase.cs:69-85` claims "deserializes directly from the
HTTP stream" (actually fully buffered `ResponseContentRead`); `DockerIgnoreFilter.cs:208` `directoryOnly`
ternary has identical branches (dead parameter — behavior correct, doc a lie); `TailBytes` 256 KiB applied as
*bytes*, can slice mid-UTF-8 → cosmetic U+FFFD at truncated tail head.

### What holds up

Better than most hand-rolled Docker clients: one shared `SocketsHttpHandler` with leak-guarded pooled
unix-socket/npipe `ConnectCallback`; split short/long-running `HttpClient` pair, correct disposal order;
`ResponseOwningStream` airtight (idempotent, sync+async, `transferred` flag prevents double-ownership); stdcopy
demux otherwise faithful (partial-read loops, frame caps, per-stream UTF-8 decoder state across frames); ustar
checksum/prefix-split/`@LongLink` correct; `X-Registry-Auth` base64 Go-compatible; `DockerIgnoreFilter` matches
moby patternmatcher (anchoring, last-match-wins `!`, `**`, char classes, NonBacktracking); build context
streams through delete-on-close temp file with race-free realpath containment on Linux; CopyFrom blocks
zip-slip; version negotiation recovers across daemon restarts; TLS custom-root validation strict-by-default.

---
## Chunk 8 — Builders Layer

**Examined:** all 39 files under `FluentDocker/Builders/` (6,306 lines, excl. model builders) + 22 under
`FluentDocker/Model/Builders/` (646 lines). Traced `UseContainer()...BuildAsync()` end-to-end (queue grouping,
validation, deferred start, mid-build cleanup); every wait condition read line by line; suspicious behaviors
verified against a live Docker daemon; 553 builder unit tests pass.

**Verdicts:** Production ready: yes with reservations — no criticals; 3 majors, all edge-path. .NET 10: strong
(correct Interlocked build latch, CT threaded through every queued op, reverse-order bounded cleanup
independent of caller cancellation). XML docs: yes on interfaces (real behavioral contracts); gaps on
IPodBuilder/IImageBuilder and concrete classes. Zero bugs: no. Resilient: **yes** — fail at resource 3 of 5 →
1-2 torn down reverse-order with per-item try/catch, bounded independent timeout, borrowed-resource
protection, actionable manifest; retry idempotent via `ResetForRetry` + re-owning by ID/name. Consumable: yes —
discoverable, excellent failure messages (log tails attached); a few silent no-ops betray the fail-fast ethos.

### Findings

**[MAJOR] `Extensions/EnvironmentExtensions.cs:32-45` — ENV/LABEL *name* part bypasses the newline guard the
value part gets.** Value is checked and escaped; name renders verbatim into `$"{name}={value}"`. A name
containing a line break introduces extra lines in the generated Dockerfile — attacker-influenced input reaching
`Environment()`/`Label()` can inject additional Dockerfile instructions. The hardening pass added
`DockerfileInstructionGuard` to FROM/RUN/USER/ARG/WORKDIR/HEALTHCHECK/EXPOSE/MAINTAINER for exactly this class
— this path is an asymmetric miss against the library's own threat model.
*Fix:* validate `name` through the guard (reject `\n`/`\r`/control chars) in `WrapValue`.
*(Cross-ref: Chunk 5 flagged the same file for weak name validation — two independent reviewers.)*

**[MAJOR] `Builders/DockerfileBuilder.FileOps.cs:45-55` — COPY-from-URL download has no timeout; can hang
`BuildAsync` forever.** `SharedHttpClient` has `Timeout = InfiniteTimeSpan` and its own doc says callers must
bound per-request; this call site passes only the caller's token, and `BuildAsync()` is routinely called with
default token. A server that accepts and stalls hangs the build indefinitely — the one unbounded I/O in an
otherwise carefully bounded pipeline. *Fix:* linked CTS with bounded default (e.g. 100s) around the download.

**[MAJOR] `Builders/DockerfileBuilder.FileOps.cs:173-209` — mixing `FromFile()`/`FromString()` with fluent
commands silently discards the fluent commands.** `.FromFile("Dockerfile").Run("apt-get ...")` → file wins,
`Run` vanishes with no error. Silently wrong images that still build. *Fix:* throw in `Validate()`/render when
both a file/string source and commands are present.

**[MINOR] `ContainerBuilder.WaitConditions.cs:340` — unchecked long-to-int cast in HTTP continuation delay** —
value above int.MaxValue wraps negative, `Task.Delay` throws AOORE — crash instead of wait/timeout.
*Fix:* `(int)Math.Min(delay, int.MaxValue)`.

**[MINOR] `ContainerBuilder.Basic.cs:52-60` — `ExposePort(string)` missing null guard** (siblings have it);
NRE from inside normalization instead of `ArgumentException`.

**[MINOR] `ContainerBuilder.ImageRef.cs:59-63` — trailing colon (`"repo:"`) yields empty tag passed to the
driver** → cryptic daemon pull error at build time. *Fix:* config-time validation error.

**[MINOR] `ImageBuilder.cs:189-197` — `ImageTag(null)` (and BuildArguments/Label) NRE on null params array.**
*Fix:* `ThrowIfNull` in the three params methods.

**[MINOR] `InternalBuilders.cs:361-365` — compose model-overlay temp file leaks if the pre-Up existence probe
throws** (probe runs before the try whose catch deletes temp files). *Fix:* widen the try block.

**[MINOR] `DockerfileBuilder.FileOps.cs:123-146` — ADD lacks the basename-collision guard COPY has** — second
rooted ADD source with same basename never copied; original rooted path renders into the Dockerfile → confusing
daemon "not found" instead of COPY's clear exception. *Fix:* same `rootedNames` guard.

**[MINOR] `ContainerBuilder.Execute.cs:30` — `ReuseIfExists()`/`DestroyIfExists()` silently ignored when
`WithName()` not set** (gated on `_name` non-empty). *Fix:* reject in `Validate()`.

**[MINOR] `Builder.Cleanup.cs:18` — one shared CTS for the whole cleanup pass** — one hung removal consumes
the budget; later resources recorded "cleanup failed: canceled". Bounded (good) but unfair.
*Fix:* per-resource slice or document budget semantics.
*(Cross-ref: same shared-budget pattern flagged at `Kernel/BuildResults` in Chunk 1.)*

**[MINOR] `ContainerBuilder.Basic.cs:97,101-107` — `WithVolume` cannot express host paths containing a colon**
(only Windows drive-letter special-cased); corrupt spec string, no `--mount` alternative. *Fix:* reject with
clear message.

**[MINOR] `InternalBuilders.cs:251` — `WithComposeFile(null)` accepted at config time**, fails later as
file/driver error. *Fix:* guard here and in `WithComposeFiles`.

**[MINOR] `Model/Builders/FileBuilder/ArgCommand.cs:40` — ARG default value not quoted** (`ARG N=a b` parses
as second token, silently corrupting the declaration). *Fix:* quote/escape like ENV values.

**[MINOR] XML docs:** `IPodBuilder` lacks param/returns; `IImageBuilder` one-liners omit format contracts
(KEY=VALUE undocumented); concrete classes omit `<inheritdoc/>` so concrete-typed usage shows no IntelliSense;
`CS1591` suppressed so coverage unenforced.

**Notes (not defects):** generated Dockerfiles CRLF on Windows (daemon tolerates); Port/Http/Process/Healthy
waits don't re-check once deadline passes mid-sleep while LogMessage does (small inconsistency); v3
wait-continuation semantics invert v2's (documented correctly; migration note would help).

### What holds up

Several obvious adversarial attacks failed under live verification: 2+-char name regex matches actual daemon
behavior; `FindExistingContainerAsync` exact-matches after Docker's substring filter (no wrong-container
reuse); `ParseImageReference` handles registry:port and digests; `-p :8080/tcp` and `ip::port` renderings
live-tested valid. Exception safety genuinely good: build latch can't be bricked, cleanup reverse-order
per-item-caught under an independent token (caller cancel cannot skip cleanup), borrowed resources never
deleted, retries re-own prior resources by ID/name. Failed starts attach log tails — excellent diagnosability.
Dockerfile injection defenses comprehensive except the one ENV/LABEL name gap; compose env-file parser matches
compose-go closely. Mock driver pack test scaffolding well designed (553 tests in 5s).

---
## Chunk 9 — Podman CLI Driver

**Examined:** `FluentDocker/Drivers/Podman/**` — PodmanCliDriverBase (+ Streaming), Binary/, all Components
(Container + Args/Operations/Parsing/StatsParsing, Image + Operations/Progress, Kubernetes, Machine +
Operations, Manifest, Network, Pod, Stream, System + Operations, Volume, Auth, PodmanContainerParser),
BuilderExtensions, PodmanCliDriverPack. Behavior diffed against the Docker CLI driver on identical port calls;
parsing verified against podman 4.9/5.0 source structs; two attack hypotheses (RFC3339Nano, ANSI clear-screen)
refuted by experiment.

**Verdicts:** Production ready: **PARTIAL** — execution engine solid; silent Docker-parity contract gaps will
surprise consumers switching drivers. .NET 10: yes (ConfigureAwait everywhere, OCE rethrown, linked-CTS timeout
vs cancel, tree kill + cidfile orphan cleanup, no pipe deadlocks). XML docs: yes, minor gaps. Zero bugs: no
(4 MAJOR + 6 MINOR). Resilient: PARTIAL — machine-stopped-mid-op surfaces as **three different signals**
depending on code path; streaming has no retryable classification. Consumable: PARTIAL — version matrix well
documented, but result-parity divergences have zero discoverability.

### Findings

**[MAJOR] `Components/PodmanCliImageDriver.cs:226-231` — `ImageListFilter.Dangling`, `Before`, `Since`,
`Labels` silently ignored.** Only `All` and `Reference` are consumed; the Docker driver emits all four filter
clauses for the same `ImageListFilter`, and podman CLI supports them. Identical fluent code returns a larger,
unfiltered image set on Podman — pruning/label-selection logic operates on wrong data with no warning.
*Fix:* append the four missing `--filter` clauses exactly as the Docker driver does.

**[MAJOR] `Components/PodmanContainerParser.cs:43-68` — `ListAsync` never populates `Container.Created` or
`State.Running`.** Parser returns only Id/Image/Name/Status; podman's `ps --format json` (verified against
v5.0.0 `ListContainer` struct) provides `Created`, `State`, and an explicit `Exited` bool — all dropped.
`containers.Where(c => c.State.Running)` returns **empty on Podman, correct on Docker**; age-based cleanup sees
`Created == default`. *Fix:* set `Running = status == "running"`, parse `Created`/`StartedAt`.

**[MAJOR] `PodmanCliDriverPack.cs:356,397,408,425,486` — `PodmanMachineNotRunningException` is never raised by
operations, contradicting the documented contract.** All five throw sites live in the pack's init/auto-start
path (macOS/Windows with machine options only). docs/troubleshooting.md:107-114 instructs
`catch (PodmanMachineNotRunningException ex) when (ex.IsTransient)` around resource builds — but a machine
stopped mid-session surfaces as `CommandResponse.Fail` with `Api.ConnectionFailed`; the documented catch never
fires. Retry/backoff written per the docs silently never engages for the most common Podman failure mode.
*Fix:* map daemon-connection errors to `Machine.NotRunning` when machine management applies, or fix the docs
and expose the transient flag on the response.

**[MAJOR] `PodmanCliDriverBase.Streaming.cs:104,188,260` — streaming failures bypass error classification
entirely.** All three variants throw `DriverException(..., ErrorCodes.Driver.CommandExecutionFailed)` — no
`FailureCode(...)` — so machine-stopped during `StreamLogsAsync`/`StreamEventsAsync`/`StreamStatsAsync` is
`DRV_xxx` while the identical failure on a buffered call is `Api.ConnectionFailed`; two variants also omit
`ErrorContext`. Same physical failure, different retry classification depending on which API was used.
*Fix:* `FailureCode(failure, ...)` + context in all three.

**[MINOR] `PodmanContainerParser.cs:188` — `StopSignal` silently null on podman 4.x** (uint in 4.9 vs string
in 5.0; `GetStringOrDefault` returns null for numbers; docs claim 4.x support). *Fix:* numeric fallback.

**[MINOR] `PodmanCliContainerDriver.Operations.cs:272-273` — exec exit-125 with stderr-only output
misclassified as infra failure, diverging from Docker** (Docker requires a daemon-error marker in stderr;
Podman only requires empty stdout). Portability break for probe scripts using exit 125. *Fix:* also require a
podman error marker (list already exists at 281-285).

**[MINOR] `PodmanCliNetworkDriver.cs:59-61` — `NetworkCreateResult.Id` holds the network *name* on Podman, the
64-hex *ID* on Docker** (podman prints response.Name; docker prints ID). Correlation keyed by Id breaks.
*Fix:* document or resolve via inspect after create.

**[MINOR] `PodmanCliSystemDriver.Operations.cs:212` — culture-sensitive `int.TryParse`** — the only numeric
parse in the Podman tree without `InvariantCulture` (contrast line 241 below it). *Fix:* add the culture args.

**[MINOR] `PodmanCliDriverBase.cs:313-319` — stdin-write failure with exit 0 yields `Success=true` with
`Error` populated**; `MergeOutputAndError` consumers would append failure text to payload data.
*Fix:* `Success = false` when `stdinFailure != null`, or log instead.

**[MINOR] `PodmanCliContainerDriver.Operations.cs:164-172` — `ExecAsync`/`BuildAsync` XML omits the 256 KiB
tail-truncation contract** (GetLogsAsync documents its own; Docker parity holds — pure doc gap).
*Fix:* one `<remarks>` sentence each.

### What holds up

Execution core is the strongest part: concurrent bounded stream drains (4 MiB cap, 256 KiB rolling tails,
Channel(256)) eliminate pipe deadlocks and unbounded memory; caller-cancel vs 5-min timeout correctly
distinguished with tree kill and cidfile orphan cleanup; sudo and registry passwords via stdin only; stdin
closed when unused. Parsing defensively dual-cased throughout (Health/Healthcheck, Entrypoint string-or-array,
machine ConfigDir object-or-string, numeric-or-string byte sizes); stats keys match podman 5's actual jstat
struct; auto-start machine management uses a per-machine-name semaphore + readiness polling. RFC3339Nano and
ANSI clear-screen attack hypotheses refuted by experiment. docs/podman.md honestly documents version-gated
flags. Residual risk concentrated where nobody tested cross-driver: **parity of results (not arguments) and a
consistent failure taxonomy**.

---

## Executive Summary (compiled from all 9 reviews)

### Release blockers (fix before any GA tag)

| # | Severity | Area | Issue |
|---|----------|------|-------|
| 1 | CRITICAL | Testing/Core | `ComposeResource` destroys borrowed `ConnectToExisting()` stacks at teardown (`ComposeService.RemoveAsync` has no borrowed guard) |
| 2 | CRITICAL | Kernel | Registry registration-failure path disposes instances it never accepted; corrupts live registrations (3 verified repros) |
| 3 | MAJOR | Docker API | Registry-authenticated `docker build` functionally dead: `X-Registry-Config` never carries passwords (`[JsonIgnore]`) and Docker Hub key never matches |
| 4 | MAJOR | Docker CLI | Culture-sensitive numeric arg interpolation corrupts `-1` sentinels (U+2212) on ICU locales — ~20 sites |
| 5 | MAJOR | Services | Compose `RefreshStateAsync` never passes `-a`: documented `Stopped` state unreachable on modern compose |
| 6 | MAJOR | Podman | `PodmanMachineNotRunningException` contract dead — documented retry pattern never fires for the most common Podman failure |
| 7 | MAJOR | Builders/Extensions | ENV/LABEL *name* injection gap in generated Dockerfiles (value guarded, name not) |

### Cross-cutting themes (single fixes, multiple payoffs)

1. **Failure-path ownership & classification is the systemic gap.** The 132-finding hardening pass hardened
   happy paths, read paths, and dispose paths; what it missed, everywhere, is who owns what on failure
   (kernel dispose-on-failure, BuildScope clear-before-dispose, compose resurrect-after-remove) and whether
   errors classify consistently (Docker NotFound dead code vs Podman; streaming vs buffered codes; ImagePull
   always-transient; machine-down = 3 different signals).
2. **Cross-driver parity of *results* is untested.** Args parity got attention; result parity didn't
   (Running/Created dropped on Podman, network Id vs name, exec-125 semantics, image filters ignored).
   A port-contract test suite running both drivers against the same assertions would catch all of these.
3. **`CS1591` suppression hides real doc rot** — `<inheritdoc/>`-on-nothing (Docker API, ~6 files), bare
   publics in Extensions, undocumented ctors in Services. Un-suppress per-project and fix what falls out.
4. **Shared cleanup budgets are unfair and lossy** (Kernel BuildResults 60s shared CTS; Builder cleanup single
   CTS; dispose-budget abandonment invisible). One hung daemon call cascades into silent leaks — per-resource
   slices + surfaced results.
5. **Two independent reviewers flagged `EnvironmentExtensions` name validation** — fix once, closes both.

### Scorecard per question (whole library)

| Question | Verdict |
|---|---|
| 1. Production ready | **Not yet.** 2 criticals + 7 majors above are small diffs (each ≤ a day); nothing architectural. |
| 2. .NET 10 best practices | **Yes** — genuinely strong: CT propagation, ConfigureAwait, Interlocked dispose, bounded buffers, no sync-over-async found anywhere. |
| 3. Docs production ready | **Nearly** — samples 100% API-accurate (307/307), links near-perfect; blockers: 2 site-root 404s, 20× banner duplication, README missing the WithPort flip, `<inheritdoc/>`-on-nothing. |
| 4. Zero bugs | **No** — 2 critical, ~18 major, ~50 minor verified findings across 9 areas. |
| 5. Resilient | **Partially** — transient classification is real and wait/retry helpers honor it; gaps: no built-in retry (documented), inconsistent failure taxonomy, cleanup-budget leaks. |
| 6. Easy to consume | **Yes** — fluent surface discoverable, exceptions actionable, failure messages exemplary (log tails attached). |
| 7. Docs findable/readable | **Yes** — one obvious path, not bloated; model-runner split is genuine hierarchy; bloat limited to banner duplication + v3.0.0 marketing + triple quick-start drift. |

*Generated by 9 parallel adversarial subagent reviews; every finding verified in code (many by executed repro
or live daemon), none taken from comments or docs on trust.*
