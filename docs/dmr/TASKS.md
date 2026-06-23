# Docker Model Runner (DMR) — Implementation Tasks

Companion to [DESIGN.md](./DESIGN.md). Tasks are grouped into phases; the table
gives dependencies, the agent recommended to drive each task, and the tests that
gate it. **All work is TDD-first** (write/extend the failing test, then the
implementation) per the project's standing rules, and every phase must end with
a green `make test` before the next begins.

Legend:
- **ID** — task identifier (referenced by `Depends`).
- **Depends** — task IDs that must complete first (`—` = none).
- **Agent** — recommended subagent type to drive the task (see §Agents).
- **Size** — rough effort (S ≤ ½ day, M ≈ 1 day, L ≈ 2–3 days).
- **Tests** — the test artifact(s) that must be green to call the task done.

---

## Phase 0 — Foundations (domain model & error plumbing)

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| F1 | `ModelReference` value object: parse/`TryParse`/`ToString` for `ai/…`, `hf.co/…`, fq registry, digests, default tag. | — | `csharp-impl` (general-purpose) | M | `ModelReferenceTests` (Unit) — full parse/round-trip matrix incl. invalid inputs |
| F2 | Management/runtime POCOs: `ModelInfo`, `RunningModel`, `ModelRunnerStatus/Version`, `ModelDiskUsage`, `ModelPruneResult`, `ModelPullProgress`. | — | `csharp-impl` | S | Compile + serialization round-trip unit tests |
| F3 | Option records: `ModelRunOptions`, `ModelConfigureOptions`, `ModelPackageRequest`, `ModelRunnerInstallOptions`, `ModelRunnerCapabilities`. (No `ModelTransport` enum — transport is an adapter detail, §8.4.) | — | `csharp-impl` | S | Compile + default-value unit tests |
| F4 | Inference DTOs (STJ-annotated), split across `Inference/*.cs`: Chat, ChatStreaming, Completion, Embeddings, common (`Usage`, `ChatMessage`, `OpenAiModel`). | — | `api-designer` | M | DTO (de)serialization tests against fixture JSON (Unit) |
| F5 | `ModelRunnerEndpoint` value object + default resolution order (env, host TCP, container DNS, socket). | F1 | `csharp-impl` | S | `ModelRunnerEndpointTests` (Unit) — URL/path building, engine-in-path toggle |
| F6 | Error codes (`ErrorCodes.Model`, `ErrorCodes.ModelInference`) + `ModelRunnerException`. | — | `csharp-impl` | S | Unit test asserting code uniqueness & exception ctor/context |
| F7 | `DriverCapabilities` additive flags `SupportsModels`, `SupportsModelInference`. | — | `csharp-impl` | S | Existing capability tests still green; new flag defaults `false` |
| F8 | `ModelBackend` open value (llama.cpp/vllm/diffusers/custom) + `LlamaCppRuntimeFlags` typed builder with range validation and `ToArgs()` rendering; expand `ModelConfigureOptions` (reset ctx, `HfOverridesJson`) + `ModelRunnerUninstallOptions`. | F3 | `csharp-impl` | M | `LlamaCppRuntimeFlagsTests` (Unit) — flag rendering, range validation throws, raw passthrough; `ModelBackendTests` |

**Phase 0 review gate:** `thiink-ddd-expert` reviews `ModelReference`/value
objects for immutability & invariants; `make test` green.

---

## Phase 1 — Ports (hexagonal interfaces)

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| P1 | `IModelManagementDriver` port. | F1,F2,F6 | `architect` (feature-dev:code-architect) | S | Interface compiles; consumed by mock in M1 |
| P2 | `IModelRuntimeDriver` port. | F1,F2,F3,F6 | `architect` | S | as above |
| P3 | `IModelInferenceDriver` port (incl. `IAsyncEnumerable` streaming sigs). | F4,F6 | `architect` | S | as above |
| P4 | Optional aggregate `IModelDriver`. | P1,P2,P3 | `architect` | S | Compile |
| P5 | `IModelApiConnection` abstraction. | F5 | `architect` | S | Implemented in A-phase; mocked in M2 |

**Phase 1 review gate:** `thiink-hexagonal-reviewer` confirms ports have no
adapter leakage and dependency direction is inward.

---

## Phase 2 — Mock infrastructure (enables TDD for everything downstream)

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| M1 | Extend `MockDriverPack` (partial) with `Mock<IModelManagementDriver/RuntimeDriver/InferenceDriver>` + `SetupModel*` helpers. | P1,P2,P3 | `test-automator` | M | Self-test: mock pack resolves model ports via kernel `SysCtl` |
| M2 | `MockModelApiConnection` (no Moq) with programmable JSON responses **and SSE script** support. | P5 | `test-automator` | M | Self-test: replays a chat + an SSE stream + a forced fault |
| M3 | `DmrFixtures` loader + fixture files (`ls.json`, `inspect.json`, `ps.json`, `df.json`, `chat.sse`, `chat.json`, `embeddings.json`, table-format fallbacks). | — | `test-automator` | S | Fixtures load as embedded resources |

**Phase 2 gate:** mocks compile and their self-tests pass; downstream phases now
have everything needed to write failing tests first.

---

## Phase 3 — CLI adapters (management + runtime)

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| C1 | `ModelJsonParser` (+ table fallback) mapping DMR CLI output → POCOs. | F2,M3 | `csharp-impl` | M | `ModelJsonParserTests` (Unit) over all fixtures + malformed inputs |
| C2 | `ExecuteStreamingCommandAsync` added to `DockerCliDriverBase` (line-streamed stdout). | — | `golang-pro`→`csharp-impl` | M | Unit test with a fake process emitting lines incrementally; existing CLI tests unaffected |
| C3 | `DockerCliModelManagementDriver` (pull/ls/inspect/rm/tag/push/package/prune/df). | P1,C1,C2 | `csharp-impl` | L | `DockerCliModelDriverTests` (Unit) — command-string assertions + parsed results via a fake executor |
| C4 | `DockerCliModelRuntimeDriver` (status/version/ps/load/unload/**configure with `--` passthrough + `--backend` + `--hf_overrides` + ctx reset**/logs/install/uninstall). | P2,C1,C2,F8 | `csharp-impl` | L | `DockerCliModelDriverTests` (Unit) — incl. configure-command assembly assertions |
| C5 | Argument-quoting/security tests for model **references** on the CLI control plane (pull/rm/configure/load). Prompts are not shell-bound — inference rides a JSON HTTP body, so shell injection is structurally impossible there. | C3,C4 | `penetration-tester` | S | Security unit tests (`"; rm -rf /`, backticks, `$()` in refs) |
| C6 | ~~(Optional) `DockerCliModelInferenceDriver` degraded one-shot via `model run`.~~ **REMOVED** — inference is HTTP-only; the CLI pack composes the HTTP inference adapter (§8.4). No CLI inference adapter, no `ModelTransport`. | — | — | — | n/a |

**Phase 3 gate:** `code-reviewer` pass on the CLI adapters; file-size check
(`wc -l` < 500 each, split if needed); `make test` green.

---

## Phase 4 — HTTP/API adapters (inference + native management)

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| A1 | `ModelApiConnection` (HttpClient, TCP + unix socket via `SocketsHttpHandler.ConnectCallback`, ping). | P5 | `csharp-impl` | M | Unit tests via loopback `HttpMessageHandler`; socket path covered by integration |
| A2 | `DockerApiModelInferenceDriver`: chat/completion (non-stream), embeddings, list-engine-models. | P3,A1,M2 | `ai-engineer` | L | `DockerApiModelInferenceDriverTests` (Unit) via `MockModelApiConnection` |
| A3 | SSE streaming: `ChatCompletionStreamAsync`/`CompletionStreamAsync` with `[DONE]` + mid-stream fault handling + cancellation. | A2 | `ai-engineer` | M | Unit tests: chunk decode, `[DONE]` stop, malformed→`StreamParseError`, cancel mid-stream |
| A4 | `DockerApiModelManagementDriver` (native `/models*`: create/list/inspect/delete + streamed pull progress). | P1,A1,M2 | `csharp-impl` | M | Unit tests via mock connection |

**Phase 4 gate:** `code-reviewer` + `thiink-resilience-auditor` (timeouts,
cancellation, no buffering, connection reuse); `make test` green.

---

## Phase 5 — Services (public façade & lifecycle)

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| S1 | Public interfaces: `IModelStore`, `IModelEngine`, `IModelInference`, `IModelRunner`, `IModelService`. | P1,P2,P3 | `api-designer` | S | Compile; drive S2/S3 |
| S2 | `ModelRunnerService` (+ `.Store/.Engine/.Inference` partials): port resolution via `SysCtl`, `CommandResponse`→exception translation, capability computation, `ChatAsync`/`ChatStreamAsync`/`EmbedAsync` ergonomics, `IAsyncDisposable`. | S1,M1,M2 | `csharp-impl` | L | `ModelRunnerServiceTests.{Store,Engine,Inference}` (Unit) via `MockDriverPack`/`MockModelApiConnection` |
| S3 | `ModelService` (`IServiceAsync`): Start=load, Stop=unload, Remove=rm, state machine + hooks; `Runner` accessor. | S1,M1 | `csharp-impl` | M | `ModelServiceTests` (Unit) — state transitions, hooks, dispose-unloads |
| S4 | `ModelRunnerEnvironment.FromEnvironment([prefix])` → `GenericOpenAiModelRunner` from injected `LLM_URL`/`LLM_MODEL` (Compose loop); enablement-detection remediation messages in `StatusAsync`. | S2 | `csharp-impl` | S | `ModelRunnerEnvironmentTests` (Unit) — env parsing, prefix mapping, missing-var errors |

**Phase 5 gate:** `thiink-clean-arch-reviewer` (business logic in service, not
adapters); `make test` green.

---

## Phase 6 — Builders & kernel wiring

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| B1 | `IModelRunnerBuilder`/`ModelRunnerBuilder` (ForModel/ContextSize/Backend/Endpoint/PullIfMissing/Build[Async]). No transport selector — `WithEndpoint` repoints inference, §13.6. | S2,F5 | `csharp-impl` | M | `ModelRunnerBuilderTests` (Unit) |
| B2 | `IModelServiceBuilder`/`ModelServiceBuilder`. | S3 | `csharp-impl` | S | `ModelServiceBuilderTests` (Unit) |
| B3 | `ModelDriverScopedBuilderExtensions` (`UseModelRunner`/`TryUseModelRunner`/`UseModel`) + top-level `Builder` shortcuts. | B1,B2 | `csharp-impl` | M | `BuilderModelExtensionsTests` (Unit) — incl. `TryUseModelRunner=false` on a pack without model support |
| B4 | Register model drivers in `DockerCliDriverPack` + `DockerApiDriverPack`; set capabilities; implement **hybrid Option A** wiring (CLI mgmt + HTTP inference under one `driverId`). | C3,C4,A2,A4 | `architect` | M | Integration-lite: kernel resolves all three ports for `"docker"` (Unit with real packs + fake binary/connection) |
| B5 | Podman capabilities → `SupportsModels=false` (until RamaLama pack). | F7 | `csharp-impl` | S | Podman pack capability test |
| B6 | `ContainerModelBuilderExtensions.WithModel(...)` — inject endpoint env (`LLM_URL`/`LLM_MODEL`) into a `ContainerBuilder` + ensure container→runner reachability (internal DNS on Desktop, `--add-host …:host-gateway` on Engine). No network/volume created (§4.2, §13.5). | B1,F5 + existing ContainerBuilder | `csharp-impl` | M | `ContainerModelWiringTests` (Unit) — env vars set, host-gateway added on Engine, `localhost` rejected for container consumers; Integration (gated): app container reaches `ai/smollm2` |

**Phase 6 gate:** `code-reviewer`; end-to-end Unit happy path
(builder→runner→mock chat) green; `make test` green.

---

## Phase 7 — Integration tests, benchmarks, docs

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| I1 | `ModelRunnerIntegrationTests` (`Category=Integration`), DMR-availability-gated, tiny model (`ai/smollm2`): pull→list→inspect→load→chat→stream→embed→unload→rm; configure ctx-size; failure paths. | B4 | `qa-expert` | L | Integration suite green where DMR present; **skips** cleanly when absent |
| I2 | CI: add DMR availability probe to the integration workflow (mirror Docker gating); keep PR runs ubuntu-only. | I1 | `devops-engineer` | M | CI dry-run; jobs skip gracefully without DMR |
| I3 | Benchmarks (`ModelRunnerBenchmarks`): SSE parse throughput, `ModelReference.Parse`, chat (de)serialization, replayed-SSE simulation via mock connection. | A3,F1,F4 | `performance-engineer` | M | `dotnet run -c Release` benchmark compiles & runs |
| I4 | Docs: usage guide + API reference page; update `make docs`; xmldoc on public surface (preview tags on inference DTOs). | S2,B3 | `api-documenter` | M | `make docs` builds; doc files ≤ 600 lines |
| I5 | README/feature matrix update + CHANGELOG for v3.2.0. | I4 | `technical-writer` | S | Manual review |

**Phase 7 gate:** full `make test` (Unit + Integration where available) green;
`make lint` clean; docs build.

---

## Phase 7b — Compose `models:` integration

Integrates with the **existing Compose builder**, not the model drivers. Additive
and independently shippable; does not block the core runner (Phases 0–7).

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| K1 | `IComposeModelBuilder` / `IComposeModelSpecBuilder` reached via the Compose builder; emit the `models:` top-level element + per-service `models:` short/long binding into rendered `compose.yaml`. | S4 + existing Compose builder | `architect` | M | `ComposeModelEmitTests` (Unit) — YAML round-trip vs. fixtures (short & long syntax, runtime_flags, context_size) |
| K2 | Parse `models:` element when attaching to an existing compose project (v3.1 attach feature); expose discovered models to the service API. | K1 | `csharp-impl` | M | `ComposeModelParseTests` (Unit) over sample compose files |
| K3 | Bind a Compose-launched model back to `IModelRunner` via `ModelRunnerEnvironment` (injected `endpoint_var`/`model_var`). | K1,S4 | `csharp-impl` | S | Unit: env mapping; Integration (gated): compose-up a service + `ai/smollm2`, chat through `IModelRunner` |
| K4 | Compose model integration tests + docs section. | K1,K2,K3 | `qa-expert` | M | `Category=Integration` (gated on DMR + compose); doc page ≤ 600 lines |

**Phase 7b gate:** `thiink-contract-reviewer` on the emitted YAML shape;
`make test` green.

---

## Phase 8 — Future / optional (not in v3.2.0 cut)

| ID | Task | Depends | Agent | Size | Tests |
|----|------|---------|-------|------|-------|
| X1 | RamaLama Podman pack: `RamaLamaCli{Management,Runtime}Driver` + `PodmanRamaLamaDriverPack`; reuse API inference driver against `ramalama serve`. | C3,C4,A2 | `csharp-impl` | L | `PodmanIntegration` suite gated on `ramalama` |
| X2 | `GenericOpenAiModelRunner` (any OpenAI-compatible endpoint + `WithApiKey`). | A2 | `ai-engineer` | M | Unit via mock connection; `SupportsManagement=false` |
| X3 | Anthropic & Ollama dialect inference adapters (selectable). | A2 | `ai-engineer` | M | Unit via dialect fixtures |
| X4 | Tool/function calling + structured outputs (`tools`, `response_format`). | A2 | `ai-engineer` | M | Unit + integration |
| X5 | Image generation port (`/engines/diffusers/v1/images/generations`). | A1 | `ai-engineer` | M | Integration (NVIDIA only) |

---

## Critical Path

```
F1,F2,F4,F6,F8 ─▶ P1,P2,P3 ─▶ M1,M2 ─▶ C3/C4 & A2/A3 ─▶ S2 ─▶ B1/B3/B4 ─▶ I1 ─▶ I4
                                                          └─▶ S4 ─▶ K1 ─▶ K2/K3 ─▶ K4 (Compose lane)
```

Parallelizable lanes after Phase 1/2:
- **Lane CLI:** C1→C2→C3→C4→C5 (+C6)
- **Lane HTTP:** A1→A2→A3 (+A4)
- **Lane Domain (early):** F1–F8 fully parallel.
- **Lane Compose:** K1→K2/K3→K4 (after S4; independent of the integration suite).

Lanes CLI and HTTP merge at **B4** (kernel wiring) and **S2** (service). The
Compose lane (Phase 7b) branches off **S4** and can ship after the core runner.

---

## Agents

| Alias used above | Concrete subagent | Why |
|---|---|---|
| `csharp-impl` | `general-purpose` (or `claude`) | No dedicated C# agent exists; general-purpose handles .NET impl with the repo conventions. |
| `architect` | `feature-dev:code-architect` | Designs port/adapter wiring against existing patterns. |
| `api-designer` | `api-designer` | Shapes the public service + DTO surface. |
| `ai-engineer` | `ai-engineer` | Inference/SSE/OpenAI semantics, generic-endpoint & dialects. |
| `test-automator` | `test-automator` | Mock infra + xUnit harness. |
| `qa-expert` | `qa-expert` | Integration suite & gating strategy. |
| `performance-engineer` | `performance-engineer` | Benchmarks. |
| `code-reviewer` | `code-reviewer` / `feature-dev:code-reviewer` | Per-phase review gates. |
| `penetration-tester` | `penetration-tester` | Arg-injection/security tests. |
| `devops-engineer` | `devops-engineer` | CI gating. |
| `api-documenter` / `technical-writer` | same | Docs & reference. |
| Architecture review gates | `thiink-hexagonal-reviewer`, `thiink-clean-arch-reviewer`, `thiink-ddd-expert`, `thiink-resilience-auditor`, `thiink-contract-reviewer` | Enforce ports/adapters, layer separation, value-object invariants, resilience, and emitted-YAML/contract shape (Compose). |

Dispatch independent lanes (CLI vs HTTP vs Domain) as **parallel agents** per
`superpowers:dispatching-parallel-agents`; use
`superpowers:test-driven-development` for every implementation task and
`superpowers:requesting-code-review` at each phase gate.

---

## Definition of Done (per task)

1. Failing test written first (TDD), then implementation.
2. `wc -l` on every touched source file < 500 (split with `partial`/multi-file if needed).
3. `make test` green on **net10.0** (the only authoritative TFM).
4. `make lint` clean.
5. Phase review-gate agent sign-off.
6. Public API has xmldoc (CS1591 honored on new code).

---

## Test Inventory (summary)

| Category | Trait | Runs in CI | Gating |
|---|---|---|---|
| Unit | `Category=Unit` | always | no external deps; mocks only |
| Integration | `Category=Integration` | PR (ubuntu) + schedule | skip unless `docker model status` OK |
| Podman integration | `Category=PodmanIntegration` | schedule | skip unless `ramalama` present (Phase 8) |
| Benchmarks | n/a (BenchmarkDotNet) | manual/scheduled | deterministic via mock connection |

---

*End of TASKS.md*
