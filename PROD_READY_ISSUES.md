# FluentDocker — Production Readiness Adversarial Review

Ten independent adversarial subagent reviews, one per functional area / driver.
Each chunk states what was examined and lists findings from **Critical → Major**,
with file:line evidence. Review questions per area: production readiness, .NET 10 best
practices, documentation quality, bug hunt, resilience/outage recovery, consumability,
docs findability.

Generated: 2026-07-10. Version reviewed: 3.2.0-preview.2 (branch `featrure/model-support`).

---

## Executive Summary

**Overall verdict: CONDITIONAL across all 10 areas — no area received an unconditional NO,
none an unconditional YES.** Zero Critical *code* findings; 2 Critical *documentation/
packaging* findings. Engineering fundamentals are consistently strong (universal
`ConfigureAwait(false)`, full cancellation propagation, no pipe deadlocks, no injection
paths, idempotent disposal, honest `ponytail:` debt markers). The blockers are edge-case
behavioral bugs, dispose-time races, silent-degradation paths, and release-packaging drift.

### Independently cross-confirmed findings (multiple agents, same hole)

1. **Pack `DisposeAsync` race** — `_drivers.Clear()` on a plain `Dictionary` read lock-free +
   `_initializeLock.Dispose()` while waiters may be queued. Found independently by the Docker
   CLI (DCLI-MAJ-4), Docker API (API-MAJ-5), Podman, and Kernel (KRN-MAJ-1/2)
   reviews — all three packs share the flaw, and the registry's own code documents why it's
   wrong. **Fix once in all three packs.**
2. **`BuildFailureManifest` drops disposed-but-not-removed resources** — found independently
   by the Builders (BLD-MAJ-4) and Kernel (KRN-MAJ-3) reviews.
3. **Docker/Podman CLI base duplication with proven drift** — the 64 MiB output cap and
   `BoundedLineReader` hardening exist only on the Docker side (POD-MAJ-1/2); the drift
   signature will keep recurring until the ~1,500-line execution core is shared.
4. **Stale TFM claim on the NuGet README** — the shipped package README still says
   ".NET 8 and .NET 10" while the release is net10.0-only by design (DOC-CRIT-1); correct
   the README to state net10.0.
5. **Silence by default** — `NullLogger` kernel defaults, silently-zeroed
   timestamps (MC-MAJ-1), opt-in exit reaper leaking running containers (TST-MAJ-1), log-only
   `EngineScope` restore failure: the library's failure-observability posture
   defaults to invisible.

### Top items to fix before a stable 3.2.0 (by severity × blast radius)

| # | Finding | Area |
|---|---------|------|
| 1 | DOC-CRIT-1 — NuGet README false TFM claim | Docs |
| 2 | DOC-CRIT-2 / DOC-MAJ-4 — unenforced release sweep; 7 typo-branch links | Docs |
| 3 | KRN-MAJ-1/2 (= DCLI-MAJ-4, API-MAJ-5) — pack dispose races, all 3 packs | Kernel/Drivers |
| 4 | SVC-MAJ-1/4 — state machine lies (`Kill`→`Stopped`, stale-state silent no-ops) | Services |
| 5 | POD-MAJ-1/2 — port Docker-side hardening to Podman base | Podman |
| 6 | POD-MAJ-3 — `PodmanMachineNotRunningException` doesn't work as documented | Podman |
| 7 | API-MAJ-1 — build context silently drops absolute-target symlinks | Docker API |
| 8 | BLD-MAJ-1 + KRN-MAJ-3/BLD-MAJ-4 — retry ownership loss + manifest hole | Builders |
| 9 | MC-MAJ-1/2 — silent date zeroing; HealthState reader corruption | Model/Common |
| 10 | TST-MAJ-1 — Ctrl-C leaks running containers by default | Testing |
| 11 | DMR-MAJ-4 — unbounded inference response body read | Model Runner |

### Question-by-question rollup

- **Production ready?** Conditional everywhere. Fix the ~30 Major findings (each estimated
  hours-to-days, none architectural) and the answer flips to yes.
- **.NET 10 best practices?** Yes — repeatedly rated "exemplary"; deductions:
  `<Nullable>annotations</Nullable>` unverified on the public surface, `DateTime` leakage in
  new DTOs, a few sync bridges.
- **Docs production ready?** Content: exceptional (zero API mismatches in 30+ verified
  samples). Delivery: broken (Liquid tags on GitHub, TFM claim, branch links).
- **Zero bugs?** No — ~25 confirmed bugs and ~20 risks across all areas, catalogued below.
- **Resilient?** Strong skeletons everywhere (timeout taxonomies, tree-kill, atomic build
  cleanup, fail-fast probes) undermined by silent-degradation defaults and a handful of
  infinite-hang paths (`CancellationToken.None` + hung daemon).
- **Easy to consume?** Fluent surface yes; frictions: kernel ceremony with stringly driver
  ids, `WithPort` semantic flip vs published package, no fluent registry-auth path, no-op vs
  throw ambiguity in capabilities.
- **Docs findable, not bloated?** Yes — 0 dead links, 0 orphans, defensible size; duplication
  confined to the 3 entry-point READMEs (with one proven drift).

---

## Chunk 1 — Podman CLI Driver

**Examined:** `FluentDocker/Drivers/Podman/**` — `PodmanCliDriverBase` partials (Attach,
Buffering, Context, Failures, Input, ProcessStart, Streaming, Unbounded), pack + lifecycle,
all Cli/Components drivers (auth, container, image, kubernetes, machine, manifest, network,
pod, stream, system, volume), `PodmanContainerParser`, binary resolver, builder extensions,
port interfaces; cross-compared against `DockerCliDriverBase` for copy-paste drift.

**Verdict: CONDITIONAL.** Engineering fundamentals are strong (universal
`ConfigureAwait(false)`, correct OCE rethrow, both pipes drained concurrently, tree-kill on
cancel/timeout, secrets off command lines, rationale-bearing XML docs). But the Podman base
is a hand-copied fork of the Docker CLI base and drift is proven: two hardening fixes on the
Docker side were never ported, and the flagship `PodmanMachineNotRunningException` resilience
story does not work as documented.

### Critical

*None confirmed.* No data-corruption, injection, or silent-wrong-result path found; the
serious defects fail loudly, just wrongly or misleadingly.

### Major

**✅ [POD-MAJ-1] 4 MiB buffered stdout cap — drift from Docker's 64 MiB fix** —
`Drivers/Podman/Cli/PodmanCliDriverBase.cs:150` — `const int MaxNonStreamingOutputBytes = 4 * 1024 * 1024;`
used for both stdout and stderr; stdout bounded read *throws* at the cap
(`PodmanCliDriverBase.Buffering.cs:33-36`). Docker base fixed exactly this:
`DockerCliDriverBase.Execution.cs:31` uses `protected virtual int ... => 64 * 1024 * 1024;`
(stderr kept at 4 MiB). Impact: `podman ps -a --format json` / `inspect` / `images` on a busy
host (>4 MiB output) hard-fails on Podman while the identical Docker call succeeds; member is
`const`/non-virtual so consumers cannot override. Fix: port the Docker change verbatim.

**✅ [POD-MAJ-2] Streaming paths have unbounded per-line memory (missing `BoundedLineReader`)** —
`PodmanCliDriverBase.Streaming.cs:84, 322, 348`, `PodmanCliDriverBase.Unbounded.cs:141` — all
streaming/unbounded reads use raw `reader.ReadLineAsync(...)`. Docker base wraps every
streaming read in `BoundedLineReader` (`DockerCliDriverBase.Streaming.cs:68-108`) capping a
single line and marking truncation. Impact: one pathological line without a newline (huge JSON
event, app dumping a blob into logs) buffers indefinitely in a `StringBuilder` → memory
exhaustion in long-running `LogsAsync`/`EventsAsync`/`StatsAsync` observers. Fix: port
`BoundedLineReader` (or hoist to a shared internal helper).

**✅ [POD-MAJ-3] `PodmanMachineNotRunningException` not raised reliably; docs promise it is** —
`PodmanCliDriverBase.Failures.cs:19-24, 49-50` vs `docs/troubleshooting.md:98-113`,
`docs/architecture.md:506` — the typed exception is only thrown from the pack auto-start path
(`PodmanCliDriverPack.cs:367, 402, 412, 427, 486`). Mid-operation machine-stop is classified
by stderr string-matching and maps to `ErrorCodes.Machine.NotRunning` **only if**
`context?.AutoStartMachine != null`; otherwise a macOS user with a stopped machine gets
`ErrorCodes.Api.ConnectionFailed` and a generic failure — never the documented type. The
instance overload `FailureCode(string, string)` also uses the component-level `Context`,
ignoring per-operation `AutoStartMachine`. The documented
`catch (PodmanMachineNotRunningException ex) when (ex.IsTransient)` never fires mid-operation.
Fix: classify machine-not-running on macOS/Windows regardless of `AutoStartMachine`, use the
effective per-op context, throw/wrap the typed exception — or fix the docs.

### Explicitly checked and cleared

- **Argument injection:** `CommandLineQuoting.QuoteArgumentIfNeeded`
  (`Common/CommandLineQuoting.cs:37-90`) implements correct `CommandLineToArgvW`-compatible
  rules; `QuotePositionalArgument` rejects leading-dash values; no shell involved. Sound.
- **Pipe deadlock / zombies:** both pipes drained concurrently; `using var process` +
  `Kill(entireProcessTree: true)`; `AttachResult.DisposeAsync` always tree-kills. Sound.
- RFC3339Nano timestamps, `ExecAsync` infra-vs-command exit-code heuristic, and
  `ManifestExists` outage-vs-absence distinction all correct and well documented.

### Answers (Podman CLI)

1. Production ready? **Conditional** — fix POD-MAJ-1/2/3 first.
2. .NET 10 best practices? **Largely yes** — marred by unbounded per-line reads and the
   non-virtual output cap.
3. XML/inline docs? **Yes, unusually good** — but external docs make a false exception promise.
4. Zero bugs? **No** — 5 confirmed bugs, 2 risks (see above).
5. Resilient? **Good skeleton**; machine-stopped-mid-operation misdiagnosed (POD-MAJ-3),
   hard-coded ready-wait.
6. Easy to consume/understand? **Component drivers yes**; base-class duplication with Docker
   is a *proven* maintenance hazard — extract the ~1,500-line execution core into one shared base.

---

## Chunk 2 — Docker Engine API (HTTP) Driver

**Examined:** `FluentDocker/Drivers/Docker/Api/**` (46 files) — `DockerApiDriverBase`
partials (ErrorHandling, Headers, Logs, Output, ResponseHandling, Stream), pack, `ApiResult`,
ApiModels, HTTP/unix-socket/npipe transport in `Connection/`, all Components (container
CopyFrom/CopyTo/Duration/ExecInspect/Networks, image Build/BuildContext/Errors/SaveAtomic,
network, volume, service, system, stream Demux, auth, RegistryAuth, TarWriter,
DockerIgnoreFilter), `Drivers/Connection/ResponseOwningStream.cs`.

**Verdict: CONDITIONAL.** Transport, streaming, demux, and tar layers are genuinely
production-grade: 100 % `ConfigureAwait(false)`, correct dual-`HttpClient`/shared-handler
lifetime with DNS-refreshing `PooledConnectionLifetime`, cancellation propagated into every
HTTP call and stream read, response-owning streams that provably don't leak on mid-open
exceptions, a stdcopy demux that handles split multi-byte UTF-8 across frames, and a CopyFrom
extractor defending CWE-59 more rigorously than most container tooling. Five Major behavioral
defects block an unconditional YES; none corrupts data on the happy path.

### Critical

*None.* Demux frame parsing, tar path traversal, hijacked-connection handling,
response-stream leaks on exception, and atomic save were all hunted and are either correct
or explicitly and correctly defended. Zero Criticals is earned here, not suspicious.

### Major

**✅ [API-MAJ-1] Build context silently drops symlinks with absolute targets** —
`Components/DockerApiImageDriver.BuildContext.cs:234` — `GetContainedSymlinkTarget` returns
`null` when `Path.IsPathRooted(linkTarget)` and the caller skips the entry with no log, no
warning, no error. The docker CLI includes symlinks in the context tar as-is (target string
preserved; resolution happens in the daemon). A context containing `/etc/…`-targeted symlinks
builds a silently different image than `docker build`. Silent data loss + CLI-parity break.
Fix: write the symlink entry with its literal target, or at minimum emit a structured warning.

**✅ [API-MAJ-2] Registry `identitytoken` flow unsupported — token registries degraded** —
`Components/DockerApiAuthDriver.cs:26` + `Components/DockerApiRegistryAuth.cs:118-124` —
`POST /auth` succeeds but the response body carrying `IdentityToken` is discarded;
`AuthConfig` serializes only username/password/email/serveraddress; `RegistryLoginConfig`
(`IAuthDriver.cs:40`) has no `IdentityToken` property. Docker Hub 2FA/PAT flows and some
cloud registries expect subsequent `X-Registry-Auth` to carry `{"identitytoken": "..."}` —
this driver re-sends raw credentials instead. Fix: parse, store, and prefer `IdentityToken`
in `HeaderFor` encoding.

**✅ [API-MAJ-3] Error contexts systematically stamped with HTTP status 0** —
`Components/DockerApiContainerDriver.CopyFrom.cs:76-79` and the same pattern in
Pull/Push/Build/Load/Import catch blocks — `CreateErrorContext(..., 0)` hardcodes `0` even
though `HttpStatusCodeOrZero(ex)` exists precisely to recover status from `DriverException`.
A 404 (no such container) is indistinguishable from a dead daemon. Defeats the otherwise
excellent error taxonomy (synthetic 408/599, `IsTransientCode`) exactly where operators need
it. Fix: thread `HttpStatusCodeOrZero(ex)` into every catch-block context; map 404→NotFound.

**✅ [API-MAJ-4] `ParseDurationNanoseconds` throws unhandled `OverflowException` on user input** —
`Components/DockerApiContainerDriver.Duration.cs:64` — `value * multiplier` in `decimal`
(always checked) overflows for inputs like `"9999999999999999999999999999h"`; the multiply
throws before the `nanos > long.MaxValue` guard. A malformed healthcheck interval escapes
`CreateAsync` as a raw `OverflowException`, breaking the driver's no-throw result contract.
Fix: wrap the multiply (or pre-clamp) and return the existing invalid-duration error.

**✅ [API-MAJ-5] Dispose-vs-use data race on `DockerApiDriverPack._drivers`** —
`DockerApiDriverPack.cs:286` vs `:172-217` — `DisposeAsync` calls `_drivers.Clear()` under
`_initializeLock`, but `SysCtl`/`TrySysCtl`/`TryResolve` read the plain `Dictionary` without
the lock; `ThrowIfDisposed` is a TOCTOU check, not a barrier. Concurrent resolve-during-
dispose = UB concurrent read/mutate of `Dictionary<K,V>`. Fix: `ConcurrentDictionary`, or
don't `Clear()` (disposing drivers suffices).

### Answers (Docker API)

1. Production ready? **Conditional** — ship after API-MAJ-1…5.
2. .NET 10 best practices? **Excellent** — socket exhaustion + DNS handled, full CT
   propagation, proper `IAsyncDisposable`, source-generated System.Text.Json (AOT-safe).
3. XML/inline docs? **Yes, unusually good** — they explain *why*, not just what.
4. Zero bugs? **No** — 5 Major; the classically dangerous parts (demux, tar
   traversal CWE-59, auth base64url, SaveAtomic) are correct.
5. Resilient? **Strong** — watchdogs, synthetic 408/599 taxonomy, terminal-evidence checks;
   gaps: opt-in idle timeout, negotiation pile-up, status-0 contexts blunting diagnosis.
6. Easy to understand? **Yes** — disciplined partials; API-MAJ-3's repetition will keep
   being re-introduced until the helper is made mandatory.

---

## Chunk 3 — Testing Packages & Overall Consumability

**Examined:** `FluentDocker.Testing.MsTest/NUnit/Xunit` (+ runner-test projects),
`FluentDocker/Testing/Core` (ResourceBase, ResourceLifecycle, OrphanCleanup,
ProcessExitReaper, DockerAvailability), packaging (`Directory.Build.props`,
`Directory.Packages.props`, csproj/NuGet metadata), Examples, and an end-to-end
consumer-ergonomics trace of 5 common tasks through the public surface.

**Verdict: CONDITIONAL.** The testing-resource core is unusually well-engineered:
timeout-bounded disposal, generation-fenced abandoned-provision cleanup, diagnostics capture
that never masks the original failure, null-safe teardown, and real-runner meta-tests proving
each framework awaits async lifecycle hooks. Classic sins (sync-over-async deadlocks,
teardown skipped on init failure, double-dispose races) were hunted and not found. What
remains: a default-config leak path for Ctrl-C'd runs, a cross-package naming trap, and
per-test sweep overhead.

### Critical

*None found* — stated deliberately: teardown-on-failure, fixture-dispose deadlock, and
double-dispose classes are affirmatively handled (`ResourceBase.DisposeAsync`
ResourceBase.cs:261–380; `ResourceLifecycle.CreateAndInitializeAsync`
ResourceLifecycle.cs:104–113; runner-test projects prove real runners await async hooks).

### Major

**✅ [TST-MAJ-1] Ctrl-C during a test run leaks RUNNING containers forever, by default** —
`Testing/Core/ProcessExitReaper.cs:131–136` + `OrphanCleanup.cs:159–160` — exit/SIGINT
reaping requires opt-in `FLUENTDOCKER_TEST_REAPER_ON_EXIT=1`; the next-run orphan sweep then
explicitly *preserves* running foreign-session containers behind a second opt-in
(`FLUENTDOCKER_REAP_RUNNING_AFTER`, OrphanCleanup.cs:393). The most common local-dev failure
mode (Ctrl-C mid-run) leaks a running container no built-in mechanism reclaims. Fix: default
the SIGINT/SIGTERM reaper ON, or document both env vars in the Quick Start.

**✅ [TST-MAJ-2] Same class name, different lifetime, across the three packages** —
`MsTestContainerFixtureBase.cs:84–115` (container **per test method**) vs
`NUnitContainerFixtureBase.cs:81–119` / `XunitContainerFixtureBase.cs:45` (container
**per test class**). A team migrating between frameworks silently flips from isolated to
shared containers or 10×'s container churn. Per-README lifetime tables are accurate but
nobody diffs READMEs. Fix: rename semantically (`*PerTestContainerBase` /
`*SharedContainerFixtureBase`) or add a cross-package lifetime matrix to all three READMEs.

**✅ [TST-MAJ-3] Orphan sweep + kernel creation runs on EVERY resource init — O(tests ×
daemon-resources) for per-test fixtures** — `ResourceBase.cs:175–185` —
`CleanupOrphansOnInit` defaults true and runs per resource: 3 label-filtered list calls plus
one inspect per label-less managed container (`OrphanCleanup.cs:138–142`). A 200-test suite
on a busy shared CI daemon: 200 kernel discoveries + 600 list calls + up to 200×N inspects.
Fix: process-wide once-per-(driver,session) sweep memoization (static `ConcurrentDictionary`).

**✅ [TST-MAJ-4] MSTest class-scoped fixture: forgetting mandatory boilerplate leaks the
container with only a console warning** — `MsTestClassContainerFixtureBase.cs:132–146,
164–205` — `CleanupClassAsync` must be manually wired via
`[ClassCleanup(ClassCleanupBehavior.EndOfClass)]`; omission → `Trace.TraceWarning` + stderr
at exit only. Combined with TST-MAJ-1 the leaked container is *running* and never swept.
Fix: throw under `FLUENTDOCKER_STRICT=1`/DEBUG, or ship an analyzer; at minimum surface as a
test-run diagnostic.

**✅ [TST-MAJ-5] Registry-authenticated pull has no fluent path and no docs** —
`Drivers/IAuthDriver.cs:20–23` vs zero auth hits in `Builders/*` and zero
auth/login/registry matches in `docs/images.md` — private-registry pull (top-5 real-world
task) requires `kernel.SysCtl<IAuthDriver>(driverId).LoginAsync(new DriverContext(...), ...)`
— driver-layer types leaking into a mainline consumer scenario. Fix:
`WithRegistryAuth(...)` on container/image builders + an images.md section.

### Answers (Testing & Consumability)

1. Testing packages production ready? **Conditionally yes** — robust, meta-tested core; fix
   TST-MAJ-1/2/3 for large suites.
2. net10-only right call? **Deliberate project decision (net8 is out of scope)** — ensure the
   requirement is stated clearly in package metadata/READMEs so restore failures are
   self-explanatory (see DOC-CRIT-1).
3. Testing READMEs production ready? **85 %** — honest and accurate, but hide the conditional
   fixture and the cross-package lifetime asymmetry.
4. Lifecycle bugs? **No deadlocks/missed teardowns found**; defects are default-config leaks
   and contract-contradicting `!`.
5. Docker down on CI? **Fails fast with actionable messages** — but MSTest re-probes per test.
6. Easy to consume end to end? **Middling.** Top frictions: two-phase kernel+builder
   ceremony with stringly-typed driver ids that differ between docs ("docker") and fixture
   defaults ("docker-cli"); the 3.2 `WithPort` host/container parameter flip that recompiles
   cleanly with swapped semantics; no fluent auth path; published-package/docs skew
   (`--prerelease` resolves 3.1.0 while all docs teach 3.2 semantics).

---

## Chunk 4 — Docker Model Runner (DMR) Vertical

**Examined:** the full model vertical — ports (`IModelBackendInfo`, `IModelInferenceDriver`,
`IModelManagementDriver`, `IModelRuntimeDriver`), CLI adapters
(`DockerCliModelDriverBase/ManagementDriver/RuntimeDriver`, `ModelJsonParser`), inference
drivers (`OpenAiModelInferenceDriver` + Streaming, `ModelApiConnection` + config/timeout
content), services (`ModelRunnerService` + Engine/Inference/Store, `ModelService`,
`GenericOpenAiModelRunner`, `ModelRunnerEnvironment`), builders (`ModelRunnerBuilder`,
`ModelServiceBuilder`, `ContainerModelBuilderExtensions`, scoped extensions), value objects
(`ModelReference`, `ModelRunnerEndpoint`, `ModelEnvName`, `ModelOperationGate`).

**Verdict: CONDITIONAL YES.** Some of the most defensively engineered preview code reviewed:
pervasive `ConfigureAwait(false)`, `[EnumeratorCancellation]`, a spec-complete SSE parser
(multi-`data:` accumulation, CRLF/CR/LF across chunk boundaries, BOM strip, decoder flush,
1 MiB caps, `[DONE]` + finish-reason truncation detection), a connect/request/first-byte/idle
timeout taxonomy, idempotent gate releasers, self-healing shared-load state machine, an
API-key-over-insecure-transport guard, and fixture-pinned drift tests for every CLI table it
scrapes. Preview status honestly labeled everywhere. Four items to fix/verify before stable.

### Critical

**None found — and not for lack of trying.** Explicitly probed and cleared:
`ModelOperationGate` deadlock/leak (`Common/ModelOperationGate.cs:62-76` — Interlocked-
idempotent releaser; builder holds one gate across inspect→pull→configure via un-gated
`*CoreAsync` internals, no re-entrance deadlock); streaming disposal chains; backend-probe
cache poisoning (faulted/canceled tasks evicted, `DockerCliModelRuntimeDriver.cs:79-141`);
shared-load races in `ModelService.StartAsync` (`Services/Impl/ModelService.cs:133-264` —
single elected loader, 2 h waiter bound, gate reset on failure); command injection (all model
names pre-constrained by `ModelReference` validation + `QuoteArgumentIfNeeded`); secret
leakage (bearer key refused over insecure non-loopback transport,
`ModelApiConnection.cs:305-326`).

### Major

**✅ [DMR-MAJ-1] `WithModel` host-gateway extra-host may shadow Docker Desktop's native DMR
DNS** — `Builders/ContainerModelBuilderExtensions.cs:71-72` — when the endpoint host is
`model-runner.docker.internal` the builder unconditionally adds
`WithExtraHost(InternalDns, "host-gateway")`; the comment claims the entry is "harmless" on
Desktop. Unsound: `--add-host` writes `/etc/hosts`, which takes precedence over Docker's
embedded DNS, so on Desktop it *redirects* the name to the host gateway — which only serves
DMR if host-side TCP (12434) is enabled (off by default). Containers built with
`WithModel(...)` on Desktop can get connection-refused on a name that would have resolved
fine. (High-confidence risk; not verified on live Desktop.) Fix: gate the extra-host on
engine detection or explicit opt-in; test on Desktop with host TCP disabled.

**✅ [DMR-MAJ-2] `RemoveAsync` failure detection reads stdout only** —
`DockerCliModelManagementDriver.cs:143` — `if (!result.Success || IndicatesRemoveFailure(result.Output))`;
the code two lines below already uses `FirstNonEmpty(result.Error, result.Output)` for the
error-code mapping. If any DMR version emits the failure message to stderr with exit 0,
`RemoveAsync` reports success on a failed removal — silent state divergence. Fix: one line —
run detection over `FirstNonEmpty(...)`.

**✅ [DMR-MAJ-3] Systemic CLI-scraping drift surface** — `DockerCliModelRuntimeDriver.cs:156-158`
(status from English phrasing "model runner is [not] running"),
`Parsing/ModelJsonParser.cs:224-241` (version parse assumes CLI-then-API order),
header-offset table parsing for `ps`/`df`, 404-body substring `LooksLikeModelMissing`
(`OpenAiModelInferenceDriver.cs:226-230`). Any DMR release rewording output flips status
detection or misclassifies errors; no `--format json` fallback. Partially mitigated by
fixture-pinned tests that fail on drift — right posture, but protects the library's CI, not
deployed consumers. Fix: prefer machine-readable output where offered; make status heuristic
fail loud (typed `ParseError`) instead of defaulting to "not running".

**✅ [DMR-MAJ-4] Non-streaming success body read is unbounded** —
`OpenAiModelInferenceDriver.cs:138` — `ReadAsStringAsync` with no size cap, in a codebase
where the *error* path caps at 64 KiB and streaming lines at 1 MiB. Time is bounded
(`TimeoutHttpContent`), memory is not: a misbehaving/hostile OpenAI-compatible endpoint
(`GenericOpenAiModelRunner` explicitly targets arbitrary remote endpoints) can feed gigabytes
into one `ChatAsync` before the deadline trips. Confirmed omission. Fix: capped-stream read
with a generous ceiling, typed failure on overflow.

### Answers (Model Runner)

1. Production ready? **Conditional** — quality exceeds most GA libraries; preview status
   honestly declared; fix DMR-MAJ-1/2/4, verify MAJ-3 against then-current DMR before stable.
2. .NET 10 best practices? **Exemplary** — shared `SocketsHttpHandler` + per-request linked-
   CTS budgets, spec-correct SSE, full `IAsyncDisposable`.
3. XML docs? **Yes, unusually thorough.**
4. Zero bugs? **No** — 1 confirmed (MAJ-4), 3 high-confidence risks
   (MAJ-1/2/3); parsing, gates, quoting withstood targeted attack.
5. Resilient? **Strong** — typed error codes, route-explaining 404s, timeout taxonomy,
   self-healing gate.
6. Easy to consume? **Yes** — capability-gated `UseModelRunner`/`UseModel` with `Try*`
   variants, clear conflict errors, one-liner chat/embed; off-pipeline design implemented
   consistently and documented.

---

## Chunk 5 — Builders (Fluent API) Layer

**Examined:** `FluentDocker/Builders/**` excluding model builders — `Builder` (+
BuildOperation/Cleanup/Validation), `ContainerBuilder` (+ Basic/Execute/ImageRef/Remediation/
Validation/WaitConditions/WaitConfiguration), `DockerfileBuilder` (+ FileOps), `ImageBuilder`,
`PodBuilder`, `InternalBuilders` (+ ComposeOperations), interfaces, driver-scoped extensions,
`DockerApiFluentBuilder`/`DockerCliFluentBuilder`/`PodmanCliFluentBuilder`, Compose builders;
against the `BuildScope`/`BuildResults` deferred-pipeline contract.

**Verdict: CONDITIONAL.** Visibly hardened through multiple passes (borrow-vs-own semantics,
retry contract, bounded cleanup, YAML-injection guards, log-tail diagnostics) with unusually
good XML docs and ~570 builder-focused tests. Adversarial reading still surfaced one
contract-violating ownership bug, a cleanup-timeout semantic diverging from its docs, a
failure-manifest integrity hole, an unverified compose attach, and a mutable-after-queue
foot-gun. All fixable in days.

### Critical

None. The deferred-op pipeline with per-scope grouping, declare-before-use validation, and
reverse-order bounded cleanup is a sound design.

### Major

**✅ [BLD-MAJ-1] VolumeBuilder loses ownership on retry re-own; NetworkBuilder does not** —
`InternalBuilders.cs:156–189` vs `:91–93` — NetworkBuilder's reuse branch restores ownership
(`CreatedResource = reownPriorAttempt`), VolumeBuilder's never sets `CreatedResource` back to
`true` when `priorAttemptCreated`. With `Builder.cs:236–237` wiring
`ForceRemoveOnFailure`/`FailureKeepReason` off `CreatedResource`, a volume created on attempt
1 is, on failed attempt 2: (a) not force-removed — leaked, violating the documented contract
(IBuilder.cs:64–65); (b) reported as `"borrowed"` in the manifest — the manifest lies.
Fix: set `CreatedResource = priorAttemptCreated` in the reuse branch, mirroring NetworkBuilder.

**✅ [BLD-MAJ-2] Queued container config is not immutable; validation runs on stale snapshots** —
`Builder.cs:126–147` + `ContainerBuilder.Basic.cs:14–21` — `UseContainer` snapshots name/
network/link refs at queue time but `ExecuteAsync` reads live fields. Stashing the
`IContainerBuilder` from the configure lambda and mutating it post-queue silently bypasses
declare-before-use ordering validation (`Builder.Validation.cs:10–24`). Fix: freeze the
builder when `UseContainer` returns, or re-snapshot at pre-flight.

**✅ [BLD-MAJ-3] `cleanupTimeout` is per-resource, not the documented total bound** —
`Builder.Cleanup.cs:25` — fresh `CancellationTokenSource(cleanupTimeout)` created *inside*
the reverse loop; 10 resources against a wedged daemon = 20 minutes of "bounded" cleanup,
while `IBuilder.cs:69–71` reads as a total bound. Same in
`InternalBuilders.ComposeOperations.cs:152`. Fix: shared deadline, or document "per resource".

**✅ [BLD-MAJ-4] BuildFailureManifest can silently omit a resource** —
`Builder.Cleanup.cs:42–47` — a service whose dispose completes without removing and with a
null keep-reason lands in *neither* `kept` nor `removed` — an orphan invisible to the very
manifest built to report orphans. Fix: exhaustive `else kept.Add(..., "disposed but not removed")`.

**✅ [BLD-MAJ-5] `ConnectToExisting` attaches to a compose project it never verifies exists** —
`InternalBuilders.cs:332–339` — attach branch returns `ComposeService` with
`initialState: Unknown`, no `compose ls` probe (probe at `:364` runs only on non-attach). A
typo'd project name "succeeds"; failures surface far from the cause, while
`BuilderInterfaces.cs:208–218` implies verification. Fix: probe and throw, or document
un-verified semantics.

### Answers (Builders)

1. Production ready? **Conditional** — fix BLD-MAJ-1/3/4/5, decide MAJ-2.
2. .NET 10 / deferred-execution practices? **Strong** — consistent `ConfigureAwait(false)`,
   cancellation threaded, sync bridges isolated; queued-config mutability is the one real
   deviation.
3. Docs production ready? **Mostly yes — best-in-class for a fluent API**, marred by
   MAJ-3 (docs promise a bound the code doesn't implement).
4. Zero bugs? **No** — 9 confirmed issues; ordering and remediation verified correct.
5. Resilient? **Good architecture**, undermined in edge cases by MAJ-1 (leak + wrong label),
   MAJ-3 (unbounded aggregate cleanup), MAJ-4 (invisible orphans).
6. Easy to consume? **Yes** — lambda-scoped sub-builders, actionable errors; foot-guns:
   position-dependent `WithWaitPollInterval`, additive budgets.

---

## Chunk 6 — Docker CLI Driver

**Examined:** `FluentDocker/Drivers/Docker/Cli/**` (excl. Model* components) —
`DockerCliDriverBase` partials (Attach, Buffering, Context, Execution, Input, StreamSources,
Streaming, Unbounded), pack + lifecycle, `CliByteParser`, `CliPruneOutputParser`, Binary/,
all Components (container Args/Inspection/Operations/Run/Stats, image Operations/Progress,
network, volume, compose Args/Info/Parsing/Top, stack, service, system + Daemon, stream,
auth, JSON line parser, timestamp parser, Parsing/).

**Verdict: CONDITIONAL YES.** One of the most carefully engineered CLI-wrapper subsystems
reviewed: the classic killers of process-driving code — pipe-fill deadlock, sync-over-async,
argument injection, unbounded memory, zombies — were each specifically hunted and are
demonstrably absent, usually with an explanatory comment proving the author knew the hazard.
The "conditional" is earned by an orphan-process window in the streaming paths, timeout
misfits, and teardown races in the pack's `DisposeAsync`.

### Critical

None confirmed. Specifically checked and cleared:

- **Pipe-fill deadlock** — every path reads stdout+stderr concurrently before/while awaiting
  exit (`Execution.cs:231–244`; `Streaming.cs:34–54`; `Execution.cs:352–354` drains stderr in
  stdout-only streaming).
- **Argument injection** — all user-controllable positional args flow through
  `QuotePositionalArgument`/`QuoteArgumentIfNeeded` (correct `CommandLineToArgvW`
  backslash-doubling, leading-dash rejection); `UseShellExecute = false` everywhere. Verified
  across container, image, network, volume, compose, stack, service arg builders.
- **Sync-over-async** — zero occurrences (grep-verified).
- **Unbounded memory** — stdout 64 MiB fail-fast (`Buffering.cs:25–43`), stderr 4 MiB
  truncate-but-drain (`:53–80`), unbounded ops keep a 256 KiB tail (`Unbounded.cs:125–149`),
  1 MiB line cap, channels bounded at 256.
- **Secrets on argv** — sudo password via stdin (`sudo -S --`), registry login via
  `--password-stdin`; attach explicitly rejects password-sudo (`Attach.cs:34–35`).

### Major

**✅ [DCLI-MAJ-1] Streaming paths start the process before the kill-guarding try/finally** —
`DockerCliDriverBase.Execution.cs:344–349` (stdout-only), `:438–444` (progress),
`StreamSources.cs:51–53` — after `StartProcessOrThrow`, the sudo password write
(`process.StandardInput.WriteLineAsync`) sits *outside* the `try/finally` owning
`KillProcessSafely`. If the write throws (broken pipe when `sudo` exits immediately), the
`using` disposes the `Process` object but never kills the child → orphaned `sudo`/`docker`.
`ExecuteProcessAsync` avoids this via exception-swallowing `TryWriteStandardInputAsync`
*inside* its guarded region (`Execution.cs:231–238`); the streaming trio didn't inherit that
discipline (`:348` also passes no CancellationToken). Fix: move stdin write inside the
guarded try.

**✅ [DCLI-MAJ-2] One-size 5-minute buffered timeout falsely kills long prune ops** —
`Execution.cs:41,49` — `DefaultBufferedCommandTimeout = 5 min` applies to every
`ExecuteCommandAsync` caller unless `RequestTimeout` is set. `docker system prune -a
--volumes` on a loaded host routinely exceeds 5 minutes → killed mid-reclaim, reported as
failure while the daemon carries on. A ponytail comment acknowledges the analogue for engine
switching (`DockerCliSystemDriver.Daemon.cs:97–98`); prune is unacknowledged. Fix: larger
explicit ceiling or route prune-class ops through the unbounded path.

**✅ [DCLI-MAJ-3] Health probe can block for the full 5 minutes** —
`Components/DockerCliSystemDriver.cs:109–132`, `DockerCliDriverPack.cs:203–223` —
`PingAsync` runs `docker version` under the same shared 5-min default; a liveness check
against a wedged daemon should fail in seconds, instead `IsHealthyAsync` may hang a readiness
loop 5 min per attempt. Fix: short internal ceiling (~10 s), as `BackendProbeTimeout` already
does for the model probe.

**✅ [DCLI-MAJ-4] `DisposeAsync` races concurrent `InitializeAsync`/`TryGetDriver`** —
`DockerCliDriverPack.Lifecycle.cs:31,38–39` — `finally { _initializeLock.Release();
_initializeLock.Dispose(); }` disposes the semaphore while a concurrent `InitializeAsync`
may be parked in `WaitAsync`; `_drivers.Clear()` mutates a plain `Dictionary` that concurrent
`TryGetDriver` readers touch unsynchronized. Fix: don't dispose the contended semaphore; swap
`_drivers` for a volatile immutable snapshot. (Same pattern flagged independently in the
kernel review — see KRN-MAJ-1/2.)

**✅ [DCLI-MAJ-5] Unbounded ops with `CancellationToken.None` + hung daemon = infinite hang** —
`Execution.cs:147–151` (documented design), consumed by stop/rm/exec/logs/cp/pull/build/run —
correct for pulls/builds, but `StopAsync(..., default)` against a hung daemon never returns;
resilience silently depends on every consumer passing a real token. Fix: operation-derived
ceilings for intrinsically bounded commands (`stop -t N` → N + grace).

### Answers (Docker CLI)

1. Production ready? **Conditional yes** — no Critical defects; fix DCLI-MAJ-1…4.
2. .NET 10 best practices? **Exemplary** — universal `ConfigureAwait(false)`, zero
   sync-over-async, timeout-vs-caller-cancel disambiguation, kill-entire-tree with
   drain-then-dispose ordering.
3. XML docs? **Yes, unusually so** — rationale comments, honest `ponytail:` debt markers.
4. Zero bugs? **No** — edge-grade defects (orphan window, dispose race, dropped logger,
   null-OS semantics) plus timeout design misfits.
5. Resilient? **Strong** — fails fast on missing binary with actionable errors, daemon-down
   maps to `Api.ConnectionFailed` with full context; caveats: MAJ-2/3/5 timeout gaps,
   English-only classification.
6. Easy to understand? **Yes** — rigid per-operation pattern, partial layout matching the
   500-line convention, dense test suite, honest debt markers.

---

## Chunk 7 — Kernel / Driver Registry / Resolution

**Examined:** `FluentDocker/Kernel/**` — `FluentDockerKernel`, `KernelBuilder`,
`DriverRegistry` (+ Dispose/Helpers), `ISysCtl`, driver builders, `BuildScope`,
`BuildResults`, `BuildFailureManifest`, `CapabilityChecks`; `Drivers/IDriver.cs`,
`IDriverPack.cs`, `IDriverInterfaceResolver.cs`, `DriverPackBase.cs`; pack lifecycle for all
three packs; resolution exception types.

**Verdict: CONDITIONAL.** Substantially better engineered than most driver registries —
reservation-based TOCTOU protection in `RegisterAsync`, dispose-budget design with
abandonment accounting, best-in-class exception-contract docs on `ISysCtl`. But the lock-free
read model the resolution path is built on is violated by the packs' own `DisposeAsync`, the
packs dispose a `SemaphoreSlim` the registry's code explicitly refuses to dispose for the
exact hazard the packs ignore, the failure manifest silently drops resources, and the
instance-ownership contract contradicts itself for user-supplied drivers.

### Critical

*None.* The architecture (kernel → registry → pack → resolver → cast; soft INSE vs hard
DriverNotFound) is sound and consistently executed.

### Major

**✅ [KRN-MAJ-1] Pack dispose mutates the "immutable" resolution dictionary without
synchronization — data race with lock-free readers** — `DockerCliDriverPack.Lifecycle.cs:31`
/ `DockerCliDriverPack.cs:293`; `DockerApiDriverPack.cs:286` vs `:184`;
`PodmanCliDriverPack.Lifecycle.cs:18` vs `PodmanCliDriverPack.cs:210` — `IDriverPack.cs:13-14`
states packs "must not mutate after initialization; resolution reads are intentionally
unlocked", which is the entire justification for `DriverRegistry.TryGetDriverPack`
(`DriverRegistry.cs:330-342`) reading unlocked. Every pack's `DisposeAsync` calls
`_drivers.Clear()` on a plain `Dictionary`. A resolver passing `ThrowIfDisposed()` a tick
before the disposer's CAS executes `TryGetValue` concurrently with `Clear()` — undefined
behavior, torn reads. Zero concurrency stress tests in `DriverRegistryTests.cs`. Fix: delete
`_drivers.Clear()` (rely on `_disposed` guard) or swap in an empty dictionary reference.

**✅ [KRN-MAJ-2] Packs dispose their `_initializeLock` SemaphoreSlim; the registry's own code
documents why that is wrong** — `DockerCliDriverPack.Lifecycle.cs:39`,
`DockerApiDriverPack.cs:293`, `PodmanCliDriverPack.Lifecycle.cs:26` —
`DriverRegistry.Dispose.cs:95` says verbatim: "Do not dispose SemaphoreSlim; waiters may
still be queued". `SemaphoreSlim.Dispose()` does not wake waiters: a concurrent
`InitializeAsync` queued behind the disposer hangs forever in `WaitAsync` or hits
`ObjectDisposedException` in its `finally`, masking the real error. Packs are public API;
`IDriverPack.cs:16` doesn't forbid concurrent calls. Fix: remove `_initializeLock.Dispose()`
from all three packs.

**✅ [KRN-MAJ-3] `BuildFailureManifest` loses resources: disposed-but-not-removed services
appear in neither list** — `Builders/Builder.Cleanup.cs:43-48` — a service whose `Dispose`
stops but does not remove it (state `Stopped`) falls through both branches and is absent from
the manifest attached to the exception (`Builder.cs:419`), while `BuildFailureManifest.cs:8-9`
promises a summary of resources observed. CI garbage-collection keyed on the manifest
under-reports exactly the resource most likely to linger. Fix: exhaustive final `else`
branch. *(Independently found by the builders review — BLD-MAJ-4; two agents, same hole.)*

**✅ [KRN-MAJ-4] Ownership contract contradiction: builder disposes user-supplied instances on
pre-acceptance failures the registry promised not to touch** — `KernelBuilder.cs:127-134` vs
`IDriverRegistry.cs:31` — the registry doc implies pre-acceptance failures (duplicate
driverId, context-ID mismatch) leave the instance usable; `KernelBuilder.BuildAsync`'s catch
disposes `currentInstance` in exactly that case, including instances supplied via
`UseCustomDriver`/`UseCustomDriverPack` (`:211-225`). With the single-use builder
(`:87-88`), a duplicate-ID typo destroys the user's driver instance with no way to rebuild;
neither `IKernelBuilder.WithDriver` nor `IDriverBuilder` documents the ownership transfer.
Fix: document "builder takes ownership on any failure", or skip disposal of user-supplied
instances pre-acceptance.

### Verified and could NOT fault

- **BuildAsync partial-failure cleanup is correct** — driver 2 of 3 failing → registry
  disposes instance 2, builder skips double-dispose via `ConditionalWeakTable` marker,
  disposes unregistered configs 3..n, then kernel dispose handles driver 1. No leak, no
  double-dispose — traced on both sides.
- **Registration TOCTOU pattern** (reserve under lock, init outside, commit/rollback under
  lock, dispose-during-init re-check) is textbook-correct.
- **`ConfigureAwait(false)` universal; no `lock` across `await`;** disposal guards correct;
  registry's refusal to reset `_disposed` on lock-timeout shows deliberate resurrection-race
  reasoning.
- **Soft/hard exception contract honored everywhere** — INSE soft, `DriverNotFoundException`
  hard with registered-IDs hint, contract exceptions pass through raw as `ISysCtl` promises.

### Answers (Kernel)

1. Production ready? **Conditional** — fix KRN-MAJ-1…4 first; after that, yes.
2. .NET 10 best practices? **Mostly exemplary** — except packs contradicting the registry's
   own stricter dispose discipline (MAJ-1/2).
3. Docs? **Yes with reservations** — exception contracts best-in-class; ownership (MAJ-4)
   undocumented.
4. Zero bugs? **No** — dispose-while-resolving race, semaphore-dispose hang, manifest
   under-reporting, misleading exception `DriverId`.
5. Resilient? **Build failures atomic with full cleanup**; daemon-down surfaces at use;
   dispose budget-bounded with abandonment accounting — but silent by default.
6. Easy to consume? **`Create().WithDockerCli().BuildAsync()` discoverable and hard to
   misuse**; invisible failure modes are forgotten `DisposeAsync` (no finalizer signal) and
   `NullLogger`-swallowed dispose failures.

---

## Chunk 8 — Model (DTOs/Value Objects) & Common (Shared Utilities)

**Examined:** `FluentDocker/Model/**` (~180 files; deep-read on `CommandResponse<T>`,
container/image/network/volume DTOs, compose models, events, `ModelReference`,
`ModelRunnerEndpoint`, `TemplateString`, `DockerUri`), all of `FluentDocker/Common/*.cs`
(JsonHelper, JsonElementExtensions, Lenient*/Tolerant* converters, CliOutputParser,
CliOutputTruncation, CommandLineQuoting, ShellArgParser, DirectoryHelper, FdOs, Option,
Result, RequestResponse, SharedHttpClient, ModelEnvName, ModelOperationGate, exceptions),
`FluentDocker/Extensions/**`.

**Verdict: CONDITIONAL.** Unusually disciplined for a v3 rewrite: converters handle
`Utf8JsonReader` state correctly, `CommandLineQuoting` survives adversarial mental execution
against both `CommandLineToArgvW` and .NET's Unix re-parser, every sampled process launch
uses `UseShellExecute=false`, `SharedHttpClient` uses `SocketsHttpHandler` +
`PooledConnectionLifetime(2min)` (the correct DNS answer), `ModelEnvName` anchors with `\z`,
and equality contracts are hash-consistent. What blocks unconditional yes: the tolerance
strategy degrades *silently with zero observability*, one tolerant converter still corrupts
reader state on drift, and two file-system utilities carry destructive-by-design behaviors
into a public API.

### Critical

None found. Probed and cleared: command injection through quoting (traced `a\"b`, trailing
backslash, empty args — all round-trip correctly), static HttpClient DNS staleness
(mitigated), null-token handling in value-type converters (live code, handled).

### Major

**✅ [MC-MAJ-1] Global date-tolerance silently zeroes timestamps with zero diagnostics** —
`Common/TolerantDateTimeOffsetConverter.cs:16-30`, `Common/JsonHelper.cs:292-299` —
`ApplyTolerantDateTimeOffsetConverters` attaches the tolerant converter to **every**
`DateTimeOffset` property of **every** type resolved through the default options — including
arbitrary user types passed through public `TryDeserialize<T>`. An unparseable value (e.g.
Podman's Go-format `"2023-01-05 10:04:22.95 +0100 CET"`) returns `default` — year 0001 — no
log, no counter, no marker. `Container.Created`, `ContainerState.StartedAt/FinishedAt`,
`Volume.Created` all exposed. Daemon/CLI drift becomes indistinguishable from genuinely-unset
dates; wait/uptime logic computes garbage silently. Fix: surface drift via a diagnostic
hook/counter; scope the modifier to FluentDocker's own DTOs.

**✅ [MC-MAJ-2] `HealthStateJsonConverter` corrupts reader alignment on non-scalar drift — the
exact bug its sibling fixed** — `Model/Containers/HealthStateJsonConverter.cs:10-19` —
returns `Unknown` for any non-matching token but never `reader.Skip()`s
`StartObject`/`StartArray`, unlike `TolerantDateTimeOffsetConverter.cs:27-28` whose comment
explains why the skip is mandatory. A drifted `Status: {...}` fails the **entire** container
inspect parse. Also asymmetric with `LenientBoolConverter.cs:36`. Fix: same two-line skip guard.

**✅ [MC-MAJ-3] `DirectoryHelper.CopyFilesRecursively` silently renames user files
(`dot_git`→`.git`, `gitmodules`→`.gitmodules`)** — `Common/DirectoryHelper.cs:22-26, 55-58` —
LibGit2Sharp *test-fixture* behavior (admitted at line 15) shipped in a public
general-sounding API. A build context containing a file named `gitmodules` gets silently
renamed. Also: `file.CopyTo` throws on pre-existing targets while `FileExtensions.CopyAll`
(`FileExtensions.cs:137`) overwrites — two public recursive-copy helpers, divergent
semantics. Fix: strip the rename map (move to test tree); unify on one copy helper.

**✅ [MC-MAJ-4] Symlink-blind recursion in delete/copy helpers** —
`Common/DirectoryHelper.cs:84-94`, `Extensions/FileExtensions.cs:131-146` —
`NormalizeAttributes` recurses with no reparse-point check and sets
`FileAttributes.Normal` on everything reachable; a directory symlink cycle → unbounded
recursion (`StackOverflowException`); a symlink escaping the tree → attributes cleared on
files **outside** the deleted directory. These run against temp dirs containers may have
written into — container-controlled filesystem content can trigger process crash or
out-of-tree mutation. Fix: skip `FileAttributes.ReparsePoint` entries (one-line guard each).

### Answers (Model & Common)

1. Production ready? **Conditional** — yes for the test-orchestration use case after
   MC-MAJ-1…4 (all small fixes); security fundamentals genuinely solid.
2. .NET 10 practices? **Strong** — `SearchValues`, `GeneratedRegex`, init-only new DTOs,
   hash-consistent equality; deductions: `annotations`-only nullable, no records, `DateTime`
   leakage.
3. Docs? **Common production-grade; Model has 27 undocumented files behind a suppressed
   CS1591**, plus one doc-vs-code contradiction.
4. Zero bugs? **No** — reader misalignment (MAJ-2), filesystem helpers (MAJ-3/4),
   Windows-quote edge, array leniency cliff; no injection path exists.
5. Resilient? **Strategy right, degradation silent where it most matters** (MAJ-1);
   diagnostics inconsistently threaded.
6. Consumable? **Coherent, not a junk drawer** — legacy uniformly `[Obsolete]`; wrinkles:
   compose DTO overlap, `ComposeServiceDefinition.Restart` reusing the container enum,
   `Model/Builders` a misplaced grab-bag.

---

## Chunk 9 — Services Layer

**Examined:** `FluentDocker/Services/**` excluding model services — `IServiceAsync`,
`IServiceCapabilities`, container/compose/image/network/volume/host/pod service interfaces,
`IEngineScope`, `ServiceRunningState`, `StateChangeEventArgs`, `ServiceHookExtensions`;
`Services/Impl/*` — `ContainerService` (+ Export/Lifecycle/Operations), `ComposeService`
(+ Capabilities/Lifecycle/Remove/Unpause), `ImageService`, `NetworkService`, `VolumeService`,
`PodService`, `HostService` (+ Operations), `EngineScope`, `StateChangeNotifier`;
`Services/Extensions/**`.

**Verdict: CONDITIONAL.** Well above the median: consistent `ConfigureAwait(false)`, a
CancellationToken reaches every traced driver call, idempotent `Interlocked` disposal with
time-budgeted cleanup, state-change handlers invoked outside the state lock with per-handler
exception isolation, and a genuinely well-engineered version/sequence-guarded inspect cache.
But the state machine can permanently lie, a removed pod can be resurrected, and compose
aggregate state is optimistic under partial failure.

### Critical

None found. No deadlocks (dispose uses `Task.Run` off-context), no unguarded event races
(all `UpdateState*` snapshot the delegate inside the lock, invoke outside), no double-dispose
faults (all eight disposables use `Interlocked.CompareExchange` + `_disposeCompleted` gate).

### Major

**✅ [SVC-MAJ-1] `KillAsync` marks `Stopped` regardless of signal semantics** —
`Impl/ContainerService.cs:323-355` — after `driver.KillAsync(...)` succeeds, line 348
unconditionally sets `Stopped`. The API invites non-lethal signals (`IContainerService.cs:149`
— "e.g. SIGKILL or SIGTERM"); `docker kill` returns on signal *delivery*, so
`KillAsync("SIGTERM")` (handler-ignored) or SIGHUP/SIGUSR1 leaves the container running while
`State == Stopped`. Worse: a subsequent `StopAsync` silently no-ops via the early return at
`:288` — stop intent dropped, container leaks as "stopped". Fix: inspect after kill (as
`UnpauseAsync` does at `:260-264`) or restrict the transition to terminal signals.

**✅ [SVC-MAJ-2] `PodService.StartAsync` missing the `Removed` guard — resurrects a removed
pod** — `Impl/PodService.cs:76-105` — `StopAsync` guards `Removed` (`:125-128`), and both
`ContainerService.StartAsync` (`:154-155`) and `ComposeService.StartAsync` (`:275-276`) throw
on `Removed`. `PodService.StartAsync` transitions `Removed → Starting`, driver fails
"no such pod", catch sets `Unknown` — the terminal state is destroyed and `Starting` hooks
fire against a nonexistent pod. Fix: one-line guard matching the siblings.

**✅ [SVC-MAJ-3] `HostService.PullImageAsync(image, tag: null)` produces a malformed inspect
reference** — `Impl/HostService.Operations.cs:57-111` — with `tag = null` the pull succeeds
but line 93 builds `inspectRef = $"{pullImage}:{pullTag}"` → `"nginx:"`, which
`docker inspect` rejects — a *successful* pull throws "Failed to inspect pulled image
'nginx:'". Fix: `pullTag ??= "latest"` or omit the colon.

**✅ [SVC-MAJ-4] Client-side state early-returns silently drop Stop/Pause after external
changes** — `Impl/ContainerService.cs:288-289` (`if (_state is Stopped or Removed) return;`)
and `:208-209` (Paused) — if the container is restarted/unpaused externally (CLI, restart
policy, daemon restart) while the wrapper holds stale state, `StopAsync`/`PauseAsync` return
*success without touching the daemon*. Combined with the 500 ms inspect cache (`:62`), a
service can report `Running` after the container died **indefinitely**, until someone calls
`InspectAsync`. Fix: gate the early return on a cheap inspect, or drop the guards and rely on
the existing `IsAlreadyNotRunning`/`IsAlreadyNotPaused` idempotency mappings.

**✅ [SVC-MAJ-5] `ExportAsync` has an undocumented ~2 GB hard-failure ceiling** —
`Impl/ContainerService.Operations.cs:145-160` — export writes a temp tar then
`File.ReadAllBytesAsync` buffers the whole filesystem into one `byte[]`; >2 GB throws
`IOException` and CLR array limits bite regardless. Interface remark warns about buffering,
not the hard failure. Fix: add `ExportToFileAsync(path)` (plumbing
`ExportToTempFileCoreAsync` already exists) and document the limit.

**✅ [SVC-MAJ-6] Compose lifecycle sets aggregate state optimistically, blind to partial
failure** — `Impl/ComposeService.cs:302` (`Running` after start), `:381` (`Stopped`),
`Unpause.cs:38` (`Running`) — a project where some services start then immediately crash is
labeled `Running`; `RefreshStateAsync` (`:211-269`) exists precisely to reconcile and
`RestartAsync` even calls it (`:426`) — but Start/Stop/Unpause never do. Fix: call
`RefreshStateAsync` after mutating operations.

### Answers (Services)

1. Production ready? **Conditional** — fix SVC-MAJ-1…6 before a stable 3.2.0.
2. .NET 10 practices? **Strong** — `ObjectDisposedException.ThrowIf`, Interlocked gates,
   handlers isolated; warts: sync `Dispose()` bridges (documented), no token to user hooks.
3. Docs? **Mostly yes, unusually honest** — document the export
   limit.
4. Zero bugs? **No** — 6 Major confirmed; export stream handling otherwise
   careful (`.partial` + atomic move); double-dispose safe everywhere.
5. Resilient? **Pull-based detection only** — `InspectAsync`/`RefreshStateAsync` reconcile
   well, but nothing detects daemon-side changes proactively and SVC-MAJ-4 means stale state
   actively drops operations. Errors are actionable.
6. Easy to consume? **Largely yes** — capabilities split clear, hooks pleasant; traps:
   no-op-vs-throw ambiguity, `compose start ≠ up`, always-throwing `follow` parameter,
   `GetHostPortAsync` returning 0 as "not bound", fresh-inspect needing an impl-cast.

---

## Chunk 10 — Documentation (README, docs/, package READMEs)

**Examined:** 33 markdown files under `docs/` (12,306 lines: 21 root pages + 12 subdir
pages), 5 READMEs, `CHANGELOG.md`, `DEVELOPMENT.md`. Mechanical checks: link resolution
(0 dead file links), orphan detection (0 orphans), anchor validation (0 real failures),
identifier sweep of every C# code fence against the compiled source tree (261 non-BCL
identifiers checked; 30+ samples verified across README, index, getting-started,
model-runner ×8, migration ×6, extensibility ×5, testing ×4, compose overlay, podman kube).

**Verdict: CONDITIONAL.** Doc *content* is dramatically better than the average pre-release
set — **zero hard API mismatches** in 30+ verified samples (method names, parameter order,
namespaces, return shapes all check out, including obscure surfaces). Navigation coherent,
preview status flagged on every page, migration corpus (~2,400 lines) honest and exhaustive.
However, the docs are wrapped in a broken *delivery* layer: a factually false TFM claim on
the NuGet README, raw Liquid tags on the GitHub reading path, 7 links pinned to a typo-named
temporary branch, and two samples that leak the very resources the library exists to clean up.
Remediation is ~1 day of mechanical fixes — packaging, not writing.

### Critical

**✅ [DOC-CRIT-1] NuGet package README claims ".NET 8 and .NET 10" on a net10.0-only release** —
`FluentDocker/README.md:141` vs `Directory.Build.props:7` (`net10.0` only) and
`CHANGELOG.md:14` (admits the bare NU1202). This file is the `PackageReadmeFile`
(`FluentDocker.csproj:11`) — it ships to nuget.org. The package's own landing page
contradicts the release's #1 breaking change; net8 users install, hit NU1202, file bugs.
Fix: correct line 141 and add the retarget warning.

**✅ [DOC-CRIT-2] The entire doc set documents an uninstallable package, gated only by manual
process** — `README.md:38-43`, `docs/index.md:134`, `docs/getting-started.md:29,41` — docs
teach host-first `WithPort` (3.2 semantics) while the only installable package (3.1.0) is
container-first; the only guard against publishing preview docs to the live Pages site is a
human checklist line (`DEVELOPMENT.md:49`). If package and docs don't ship atomically, users
get docs provably wrong for every installable version. Fix: block release on a scripted check
(grep for `featrure/model-support` + `preview-banner` returning zero) wired into the release
job.

### Major

**✅ [DOC-MAJ-1] Raw Jekyll Liquid tags render as literal text on GitHub.com** — all 33 pages
contain `{% include preview-banner.html %}` (grep -L returned zero exceptions); the root
README deep-links `docs/*.md` as relative repo links, making GitHub's renderer — which does
not process Liquid — a first-class path. `docs/getting-started.md:29` even says "see the note
above" referring to a banner invisible there. The most important caveat (don't run against
3.1.0) disappears exactly where most users read. Fix: plain markdown blockquote (renders in
both), or point README links at the Pages site.

**✅ [DOC-MAJ-2] Two samples leak the resources the library exists to clean up** —
`docs/model-runner.md:197-202` (`.Build()` with `BuildResults` discarded — never disposed)
and `docs/model-runner-compose.md:69-71` (sync `.Build()` discarded inside a `try/finally`
that scrupulously deletes the temp overlay file but never tears down the compose project).
`BuildResults` is `IAsyncDisposable`; every other sample preaches `await using` +
`BuildAsync()`. Copy-paste orphans containers/compose projects — in a doc set whose own
troubleshooting page has a section for leftover-name conflicts. Fix: `await using var results
= await ...BuildAsync();` in both.

**✅ [DOC-MAJ-3] Unverifiable 2016-era compatibility claim** — `docs/getting-started.md:50`
("Docker Engine API 1.24+ (Docker Engine 1.12+)") — the API driver negotiates dynamically
with no 1.24 floor (`DockerApiConnection.cs:46-93,355-367`); the same line requires Compose
V2 (2021+); the CLI driver shells to `docker compose`/`docker model` subcommands Engine 1.12
doesn't have; CI only tests current engines. Fix: state what CI actually exercises or delete
the number.

**✅ [DOC-MAJ-4] Seven links hardcode a temporary branch whose name is itself a typo** —
`README.md:41,171,182`, `docs/index.md:134`, `docs/_includes/preview-banner.html:2`,
`docs/migration.md:463`, +1 — all point at `featrure/model-support` (typo confirmed in
`git branch -a`). On merge+delete, 7 links across the two most-read files 404; until then
the typo is publicly visible in the flagship README. Fix: sweep to permalinks at merge,
enforced per DOC-CRIT-2.

**✅ [DOC-MAJ-5] Three hand-maintained entry-point documents with *proven* drift** —
`README.md` (273 ln), `docs/index.md` (254 ln), `FluentDocker/README.md` (149 ln) each
independently restate quick start, install, driver matrix, model-runner pitch; testing
READMEs (616 ln) restate `docs/testing/*` (2,368 ln). Realized drift: DOC-CRIT-1's .NET 8
claim survives only in the NuGet copy; `FluentDocker/README.md:10-22` install section carries
no 3.1-vs-3.2 caveat while the root README shouts it. Fix: root README canonical; NuGet
README reduced to install + 1 sample + links; version/TFM strings in the release-checklist
grep.

### What did NOT fail (adversarial confirmation)

- **Sample accuracy:** 30+ samples conceptually compilable; zero renamed/removed-API leaks;
  v2 patterns confined to clearly-labeled OLD blocks.
- **Links:** 0 dead relative links, 0 orphan pages, all anchors valid across 33 files.
- **Vaporware check:** api-reference.md is genuinely CI-generated (`pages.yml:46-48` runs
  xmldocmd).
- **Coverage:** every 3.2 CHANGELOG "Added" item traced to a doc page.
- **Honesty:** CHANGELOG carries Security and Known-issues sections; migration guide's
  HIGH-impact table and host-first port row do not soft-pedal breakage.

### Answers (Documentation)

1. Production ready for v3? **Conditional: no as-is; yes after CRIT-1/2 + MAJ-1/2/4 (~1 day
   of mechanical fixes).**
2. Accuracy? **Exceptional** — zero hard API mismatches in 30+ verified samples.
3. Complete? **Yes** — every feature documented, everything documented exists.
4. Findable? **Structurally <5 min README→first container** — sabotaged on GitHub.com by raw
   Liquid tags and by the package not existing on NuGet.
5. Bloated/fragmented? **Defensible at 33 files/12.3k lines** — real duplication confined to
   the 3 entry-point READMEs with one proven drift; three files at exactly the 600 cap.
6. Migration story? **Strong** — ~2,400 honest lines, sufficient for a v2 user.
7. Preview flagged? **Exemplary in substance, invisible on the GitHub render path; removal at
   GA rests on an unenforced checklist.**

---
