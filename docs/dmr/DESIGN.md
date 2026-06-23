# Docker Model Runner (DMR) Support — Design

> Status: **Draft for review**
> Target release: FluentDocker **v3.2.0**
> Authors: design generated 2026-06-22
> Scope: Add first-class support for running and consuming local LLMs through
> Docker Model Runner (and, by extension, any other container engine that
> exposes an equivalent "model runner" capability — e.g. Podman via RamaLama).

---

## Table of Contents

1. [Goals & Non-Goals](#1-goals--non-goals)
2. [Background: What Docker Model Runner Actually Is](#2-background-what-docker-model-runner-actually-is)
3. [Design Principles](#3-design-principles)
4. [Architecture Overview](#4-architecture-overview)
5. [The `IModelRunner` Interface Family (Public API)](#5-the-imodelrunner-interface-family-public-api)
6. [Driver Ports (Internal Hexagonal Ports)](#6-driver-ports-internal-hexagonal-ports)
7. [Domain Model & DTOs](#7-domain-model--dtos)
8. [The CLI Driver (Docker `model` plugin)](#8-the-cli-driver-docker-model-plugin)
9. [The HTTP/API Driver (OpenAI-compatible endpoint)](#9-the-httpapi-driver-openai-compatible-endpoint)
10. [Inference Connection & Endpoint Resolution](#10-inference-connection--endpoint-resolution)
11. [Streaming (SSE) Design](#11-streaming-sse-design)
12. [Services & Lifecycle](#12-services--lifecycle)
13. [Builders & Fluent API](#13-builders--fluent-api)
14. [Kernel Registration, Resolution & Capabilities](#14-kernel-registration-resolution--capabilities)
15. [Error Handling & Error Codes](#15-error-handling--error-codes)
16. [Other Container Engines (Podman / RamaLama / Generic OpenAI)](#16-other-container-engines-podman--ramalama--generic-openai)
17. [Security Considerations](#17-security-considerations)
18. [Performance Considerations](#18-performance-considerations)
19. [Testing Strategy](#19-testing-strategy)
20. [Configuration & Runtime Flags](#20-configuration--runtime-flags)
21. [Inference Backends (Engines)](#21-inference-backends-engines)
22. [Docker Compose Integration (`models:` element)](#22-docker-compose-integration-models-element)
23. [Enablement & Installation](#23-enablement--installation)
24. [File & Namespace Layout](#24-file--namespace-layout)
25. [Backwards Compatibility & Versioning](#25-backwards-compatibility--versioning)
26. [Open Questions & Future Work](#26-open-questions--future-work)
27. [Appendix A: Full Interface Listings](#appendix-a-full-interface-listings)
28. [Appendix B: `docker model` Command Reference](#appendix-b-docker-model-command-reference)
29. [Appendix C: DMR REST API Reference](#appendix-c-dmr-rest-api-reference)
30. [Appendix D: Sample JSON Payloads](#appendix-d-sample-json-payloads)
31. [Appendix E: Full Configuration Reference (flags, defaults, env vars)](#appendix-e-full-configuration-reference-flags-defaults-env-vars)

---

## 1. Goals & Non-Goals

### 1.1 Goals

* Provide a **fluent, idiomatic FluentDocker API** to manage and consume local
  LLMs through Docker Model Runner, consistent with the existing
  `Builder → WithinDriver → UseXxx` pattern.
* Support the **two distinct DMR surfaces**:
  * **Model management / distribution** — `docker model pull / ls / inspect / rm / tag / push / package / df / prune` and the native `/models*` socket endpoints (pull, list, inspect, delete).
  * **Inference** — the OpenAI-compatible REST API on `:12434`
    (`/engines/llama.cpp/v1/chat/completions`, `/completions`, `/embeddings`,
    `/models`), with optional Anthropic- and Ollama-compatible dialects.
* Respect **Interface Segregation**: split the user-facing `IModelRunner`
  concept into small capability interfaces so different engines / transports can
  implement only the parts they support.
* Plug into the existing **kernel / driver-pack / `IDriverInterfaceResolver`**
  machinery — model drivers are *just another resolvable interface*, exactly
  like `IVolumeDriver` or `IImageDriver`.
* Provide **both a CLI driver and an HTTP/API driver**, mirroring the existing
  `Drivers/Docker/Cli` and `Drivers/Docker/Api` split.
* Be **engine-agnostic at the port level** so Podman (RamaLama), a bare
  llama.cpp server, vLLM, or any OpenAI-compatible endpoint can be added later
  without touching the public API.
* Ship with **unit, integration, and benchmark tests** following the repo's
  established xUnit v3 / Moq / `[Trait("Category", …)]` conventions.

### 1.2 Non-Goals

* We do **not** implement our own inference engine; we orchestrate DMR / llama.cpp.
* We do **not** build a full OpenAI SDK. We expose the minimal request/response
  DTOs needed for chat, completion, and embeddings. Power users can still point
  any OpenAI client at the endpoint URL we surface.
* We do **not** support image generation (Diffusers) or vLLM-specific features
  in the first cut — but the port design leaves room (see §22).
* GPU provisioning, driver installation (`docker model install-runner`), and
  Docker Desktop enablement are **surfaced** (status/version/install) but the
  host setup itself is the user's responsibility.

---

## 2. Background: What Docker Model Runner Actually Is

Docker Model Runner (DMR) is a built-in inference engine shipped with Docker
Desktop (and installable on Docker Engine CE / Linux). It lets you pull and run
LLMs locally using the same `docker` CLI, and serves them behind an
**OpenAI-compatible REST API**.

Key facts that shape this design:

| Aspect | Detail | Design impact |
|---|---|---|
| **Engine** | Built on `llama.cpp` (default). vLLM (Safetensors) and Diffusers exist but are NVIDIA-only. | Default backend is llama.cpp; backend is a configurable string, not an enum we hard-bind. |
| **Acceleration** | Metal on Apple Silicon via *host-based* execution (not in a VM). CUDA / ROCm / Vulkan on Linux/Windows. | We never assume GPU; memory checks are opt-out (`--ignore-runtime-memory-check`). |
| **Distribution** | Models are **OCI artifacts** from Docker Hub `ai/` namespace, or pulled directly from Hugging Face (`hf.co/...`). | Model references must parse `ai/qwen3:tag`, `hf.co/org/repo`, and arbitrary `registry/ns/name:tag`. |
| **CLI** | `docker model <pull|run|ls|ps|inspect|rm|configure|tag|push|package|unload|df|status|version|logs|install-runner>`. | The CLI driver maps each verb to `docker model …`. |
| **REST** | OpenAI-compatible on `localhost:12434` (host, TCP) or `model-runner.docker.internal:12434` (from containers), plus a unix socket. Path prefix `/engines/{engine}/v1/...` or `/engines/v1/...`. | The API driver targets a configurable base endpoint; engine name is part of the path. |
| **Native model API** | `POST /models/create`, `GET /models`, `GET /models/{ns}/{name}`, `DELETE /models/{ns}/{name}` over the Docker socket. | The API driver can manage models too (not just inference). |
| **Other dialects** | Also exposes Anthropic-compatible (`/anthropic/v1/messages`) and Ollama-compatible (`/api/chat`, `/api/tags`, …) endpoints. | Out of scope for v1 surface, but endpoint model leaves room (§16, §22). |
| **Enablement** | Docker Desktop toggle, or `docker desktop enable model-runner --tcp <port>`; `docker model install-runner` on Engine. | We provide `StatusAsync`/`VersionAsync`/`InstallRunnerAsync` to detect/enable. |

See [Appendix B](#appendix-b-docker-model-command-reference) and
[Appendix C](#appendix-c-dmr-rest-api-reference) for the exhaustive command and
endpoint references.

### 2.1 Two surfaces, two transports

The single most important architectural observation:

```
                       ┌─────────────────────────────────────────┐
                       │            Docker Model Runner            │
                       └─────────────────────────────────────────┘
        MANAGEMENT surface                         INFERENCE surface
   (pull / ls / rm / configure …)            (chat / completion / embed)
            │                                          │
   ┌────────┴────────┐                       ┌─────────┴──────────┐
   │   docker CLI    │                       │  OpenAI REST API   │
   │ `docker model …`│                       │  :12434/engines/…  │
   └────────┬────────┘                       └─────────┬──────────┘
            │                                          │
   also: native /models* over docker.sock     also: Anthropic / Ollama dialects
```

* The **management surface** is lifecycle/control oriented: it's transactional,
  request/response, and is naturally a *CLI* concern (but also has native socket
  endpoints).
* The **inference surface** is data-plane oriented: it's high-throughput,
  often **streaming**, and is naturally an *HTTP* concern.

A monolithic `IModelRunner` would force every implementation to support both.
By segregating them, the Docker CLI driver handles management/runtime, an HTTP
driver handles inference (composed *into* the CLI driver pack, since the `docker
model` CLI cannot stream or embed), and the runner presents them as one — while a
constrained engine (e.g. a bare llama.cpp server) can implement inference only.
Which transport backs each port is an internal adapter decision, never a
caller-facing choice.

---

## 3. Design Principles

1. **Interface Segregation (ISP).** Per the project's standing rule
   ("Interfaces shall be quite small, use multiple if we have plugins/objects
   that only need to implement partial"), `IModelRunner` is decomposed into
   `IModelStore`, `IModelEngine`, and `IModelInference`. The aggregate
   `IModelRunner` exists only as a convenience composition.
2. **Hexagonal ports & adapters.** Public service interfaces are the
   application API; **driver ports** (`IModel*Driver`) are the hexagonal ports;
   CLI/HTTP drivers are the adapters. The kernel is the composition root that
   wires ports to adapters per `driverId`.
3. **Mirror existing conventions exactly.** Everything follows the shapes
   already established by `IVolumeDriver` / `VolumeService` / `DockerCliVolumeDriver`:
   `CommandResponse<T>` returns, `DriverContext` first arg, `JsonHelper` parsing,
   `QuoteArgumentIfNeeded`, `kernel.SysCtl<T>(driverId)` resolution.
4. **Async-first, cancellation-aware.** Every I/O method is `Task`-returning and
   takes a `CancellationToken` (last param, defaulted). Streaming uses
   `IAsyncEnumerable<T>`.
5. **Transport-agnostic public API.** Whether a runner is backed by CLI, HTTP,
   or a future engine is invisible to callers. The same `IModelRunner` is
   returned regardless.
6. **Fail soft at the driver, throw at the service.** Drivers return
   `CommandResponse<T>` (never throw for expected failures); services translate
   failures into typed exceptions (`DriverException` / new `ModelRunnerException`).
7. **No new heavyweight dependencies.** Inference DTOs use `System.Text.Json`
   via the existing `JsonHelper`. No OpenAI SDK dependency.
8. **File size discipline.** No source file exceeds 500 lines; large surfaces
   (e.g. the inference DTOs, the CLI driver) are split with `partial class` /
   multiple files, exactly as `DockerApiDriverTests` and `ContainerBuilder` are.

---

## 4. Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────────┐
│                              Consumer code                                 │
│   var runner = new Builder().WithinDriver("docker").UseModelRunner()       │
│                   .ForModel("ai/qwen3").WithContextSize(8192).Build();      │
│   var reply  = await runner.ChatAsync("Hello");                            │
└───────────────────────────────┬──────────────────────────────────────────┘
                                 │ public façade
                 ┌───────────────▼─────────────────┐
                 │  IModelRunner  (composition of)  │
                 │  ├─ IModelStore     (mgmt)       │   Services/  (public)
                 │  ├─ IModelEngine    (control)    │
                 │  └─ IModelInference (data plane) │
                 └───────────────┬─────────────────┘
                                 │ resolves ports via kernel.SysCtl<T>(driverId)
        ┌────────────────────────┼─────────────────────────────┐
        ▼                        ▼                              ▼
┌────────────────┐    ┌────────────────────┐        ┌────────────────────────┐
│ IModelManagement│    │  IModelRuntime     │        │  IModelInference        │  Drivers/  (ports)
│     Driver      │    │     Driver         │        │     Driver              │
└───────┬────────┘    └─────────┬──────────┘        └───────────┬────────────┘
        │ adapter               │ adapter                       │ adapter
        ▼                       ▼                               ▼
┌────────────────┐    ┌────────────────────┐        ┌────────────────────────┐
│ DockerCliModel │    │  DockerCliModel    │        │  DockerApiModel         │
│ ManagementDrv  │    │  RuntimeDriver     │        │  InferenceDriver        │
│ (docker model  │    │  (docker model     │        │  (HTTP :12434 OpenAI)   │
│  pull/ls/rm…)  │    │   run/ps/cfg…)     │        │                         │
└───────┬────────┘    └─────────┬──────────┘        └───────────┬────────────┘
        │                       │                                │
        ▼                       ▼                                ▼
  docker CLI process      docker CLI process            IModelApiConnection
  (DockerCliDriverBase)   (DockerCliDriverBase)         (HttpClient → :12434)
```

* The **public layer** (`Services/`) returns `IModelRunner` / `IModelService`.
* The **port layer** (`Drivers/`) defines `IModelManagementDriver`,
  `IModelRuntimeDriver`, `IModelInferenceDriver`.
* The **adapter layer** has Docker CLI adapters (management + runtime) and a
  Docker API adapter (inference + native management). Inference is **HTTP-only**:
  the `docker model` CLI cannot stream tokens or embed, so the CLI driver pack
  composes the HTTP inference adapter to satisfy `IModelInferenceDriver`. There is
  no CLI inference adapter — see §8.4.

### 4.1 Why three ports (not one, not five)

| Port | Concern | Backed by | Streaming? |
|---|---|---|---|
| `IModelManagementDriver` | Distribution & local store (pull/list/inspect/rm/tag/push/package/df/prune) | CLI `docker model …` **or** native `/models*` socket | pull only (progress) |
| `IModelRuntimeDriver` | Runner control (run/load, configure, ps, unload, status, version, logs, install-runner) | CLI `docker model …` | logs only |
| `IModelInferenceDriver` | Data plane (chat, completion, embeddings, engine model list) | HTTP `:12434/engines/…/v1` | chat/completion |

Three is the natural seam: management and runtime are both CLI-shaped but have
very different *consumers* (DevOps vs. app code) and lifetimes, while inference
is a wholly different transport. Splitting further (e.g. a separate
`IModelTaggingDriver`) would over-fragment; merging management+runtime would
mix model-distribution concerns with runner-process concerns.

### 4.2 Relationship to Containers, Networks, and Volumes

A common question: *does a model plug into the existing network/volume/container
machinery, or is it a separate subsystem?* The answer is **three-layered**, and
getting it right keeps us from modeling a lie.

**At the resource / data-plane level, a model is a parallel subsystem — by
necessity, because DMR itself models it that way:**

| Existing primitive | Used by a model? | Why |
|---|---|---|
| **Container** | **No** | `docker model ps` is a distinct lifecycle from `docker ps`; no `exec`/`attach`, no container namespace. On macOS the runner is a **host process** (llama.cpp/Metal), not even in the VM. |
| **Network** | **Not directly** | A model never joins a user-defined network. It is reached at a **fixed endpoint**: `model-runner.docker.internal:12434` (Desktop, from containers), `172.17.0.1:12434` (Engine bridge gateway), or `localhost:12434` (host TCP). |
| **Volume** | **No** | Weights live in DMR's **own OCI model store**; `docker model df` is separate from volume / `system df`. Nothing is bind/volume-mounted. |
| **Image** | **Concept only** | Models are OCI artifacts (`ai/…`), but in a **separate store** — you cannot `docker run` a model or `docker image ls` it. |

This is precisely why the design gives models their own driver ports
(`IModelManagementDriver` / `IModelRuntimeDriver`) and their own store/endpoint
rather than reusing `IVolumeDriver` / `INetworkDriver`. Trying to attach a
`Network` or `Volume` to a model would misrepresent the runtime.

**At the abstraction level, a model is fully integrated.** `IModelService`
derives from `IServiceAsync` (§12.2), so a model handle lives in the *same*
`FluentDockerKernel`, is reached through the *same* `Builder.WithinDriver(...)`
scope, is resolved through the *same* `IDriverInterfaceResolver` machinery as
volumes/networks, and participates in the *same* `ServiceRunningState` hook /
state-change pipeline. You can therefore sequence
`DB container → model → app container` through one consistent lifecycle with
`using`-based teardown.

**At the consumption level is where the subsystems actually touch** — the bridge
object is the **`ModelRunnerEndpoint`** (a URL + transport), *not* a shared
network or volume. There are three seams:

1. **Compose `models:` binding (§22)** — a service (container) declares a model
   dependency; DMR injects `LLM_URL` / `LLM_MODEL` into that container's
   environment. Declarative; the primary integration path.
2. **Imperative container → model env injection** — a `ContainerBuilder`
   extension that injects the resolved endpoint into a container's env and
   ensures reachability (host-gateway DNS). See §13.5.
3. **`ModelRunnerEnvironment.FromEnvironment()` (§20.3)** — the inverse: code
   *inside* a model-bound container reconstructs an `IModelRunner` from the
   injected env vars. Closes the loop.

```
   ┌────────────┐   consumes endpoint    ┌──────────────────────┐
   │ Container  │ ─────────────────────▶ │  Model (DMR endpoint │
   │ (network,  │   LLM_URL / :12434     │   :12434, own store) │
   │  volumes)  │ ◀───────────────────── │  no network/volume   │
   └────────────┘   ModelRunnerEnvironment└──────────────────────┘
        │ shares                                   │ shares
        └──────────── IServiceAsync / Kernel ──────┘
                (lifecycle, hooks, builder scope)
```

> **Rule of thumb:** models share *plumbing* (kernel, builder, service
> lifecycle) with containers/networks/volumes, but not *substance* (no network,
> no volume, no container). They meet the rest of the system through an
> **endpoint**, consumed by containers — never by sharing a network or mount.

---

## 5. The `IModelRunner` Interface Family (Public API)

These live in `FluentDocker/Services/` (public surface, like `IVolumeService`).

### 5.1 `IModelStore` — distribution & local store

```csharp
namespace FluentDocker.Services;

/// <summary>
/// Model distribution and local store operations: pull, list, inspect, remove,
/// tag, push, package, prune, and disk usage. Maps to `docker model
/// pull/ls/inspect/rm/tag/push/package/prune/df` and the native /models* API.
/// </summary>
public interface IModelStore
{
  Task<ModelInfo> PullAsync(ModelReference model,
      IProgress<ModelPullProgress> progress = null,
      CancellationToken cancellationToken = default);

  Task<IReadOnlyList<ModelInfo>> ListAsync(
      CancellationToken cancellationToken = default);

  Task<ModelInfo> InspectAsync(ModelReference model,
      CancellationToken cancellationToken = default);

  Task RemoveAsync(ModelReference model, bool force = false,
      CancellationToken cancellationToken = default);

  Task TagAsync(ModelReference source, ModelReference target,
      CancellationToken cancellationToken = default);

  Task PushAsync(ModelReference model,
      CancellationToken cancellationToken = default);

  Task<ModelInfo> PackageAsync(ModelPackageRequest request,
      CancellationToken cancellationToken = default);

  Task<ModelPruneResult> PruneAsync(bool all = false,
      CancellationToken cancellationToken = default);

  Task<ModelDiskUsage> DiskUsageAsync(
      CancellationToken cancellationToken = default);
}
```

### 5.2 `IModelEngine` — runner control plane

```csharp
namespace FluentDocker.Services;

/// <summary>
/// Control-plane operations against the runner itself: status, version,
/// list running models, load/unload, configure, logs, and (engine-only)
/// install. Maps to `docker model status/version/ps/run -d/unload/configure/
/// logs/install-runner`.
/// </summary>
public interface IModelEngine
{
  Task<ModelRunnerStatus> StatusAsync(
      CancellationToken cancellationToken = default);

  Task<ModelRunnerVersion> VersionAsync(
      CancellationToken cancellationToken = default);

  Task<IReadOnlyList<RunningModel>> ListRunningAsync(
      CancellationToken cancellationToken = default);

  /// <summary>Loads (and optionally keeps resident) a model. Mirrors
  /// `docker model run -d` / the runner's load behaviour.</summary>
  Task LoadAsync(ModelReference model, ModelRunOptions options = null,
      CancellationToken cancellationToken = default);

  Task UnloadAsync(ModelReference model, bool all = false,
      CancellationToken cancellationToken = default);

  Task ConfigureAsync(ModelReference model, ModelConfigureOptions options,
      CancellationToken cancellationToken = default);

  IAsyncEnumerable<string> LogsAsync(bool follow = false,
      CancellationToken cancellationToken = default);

  /// <summary>Docker Engine (CE) only — installs the runner. No-op / not
  /// supported on Docker Desktop, where the toggle is used instead.</summary>
  Task InstallRunnerAsync(ModelRunnerInstallOptions options = null,
      CancellationToken cancellationToken = default);
}
```

### 5.3 `IModelInference` — data plane (OpenAI-compatible)

```csharp
namespace FluentDocker.Services;

/// <summary>
/// OpenAI-compatible inference: chat completions (uni + streaming), text
/// completions, and embeddings. Backed by the REST endpoint on :12434.
/// </summary>
public interface IModelInference
{
  Task<ChatCompletionResponse> ChatCompletionAsync(
      ChatCompletionRequest request,
      CancellationToken cancellationToken = default);

  IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(
      ChatCompletionRequest request,
      CancellationToken cancellationToken = default);

  Task<CompletionResponse> CompletionAsync(
      CompletionRequest request,
      CancellationToken cancellationToken = default);

  IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(
      CompletionRequest request,
      CancellationToken cancellationToken = default);

  Task<EmbeddingsResponse> EmbeddingsAsync(
      EmbeddingsRequest request,
      CancellationToken cancellationToken = default);

  /// <summary>The OpenAI `/models` listing as served by the engine (distinct
  /// from <see cref="IModelStore.ListAsync"/>, which is the local OCI store).</summary>
  Task<IReadOnlyList<OpenAiModel>> ListEngineModelsAsync(
      CancellationToken cancellationToken = default);
}
```

### 5.4 `IModelRunner` — the aggregate façade

```csharp
namespace FluentDocker.Services;

/// <summary>
/// Convenience façade composing store + engine + inference, plus ergonomic
/// helpers bound to a default model. This is what <c>UseModelRunner()</c>
/// returns. Implementations MAY support only a subset of the base interfaces;
/// callers can feature-detect via <see cref="Capabilities"/>.
/// </summary>
public interface IModelRunner : IModelStore, IModelEngine, IModelInference, IAsyncDisposable
{
  /// <summary>The default model bound at build time (nullable).</summary>
  ModelReference DefaultModel { get; }

  /// <summary>The resolved inference endpoint (e.g. http://localhost:12434).</summary>
  Uri Endpoint { get; }

  ModelRunnerCapabilities Capabilities { get; }

  /// <summary>One-shot chat against the default model. Convenience over
  /// <see cref="IModelInference.ChatCompletionAsync"/>.</summary>
  Task<string> ChatAsync(string prompt,
      CancellationToken cancellationToken = default);

  /// <summary>Streaming token-by-token chat against the default model.</summary>
  IAsyncEnumerable<string> ChatStreamAsync(string prompt,
      CancellationToken cancellationToken = default);

  /// <summary>Embed a single string with the default (or specified) model.</summary>
  Task<IReadOnlyList<float>> EmbedAsync(string text,
      ModelReference model = null,
      CancellationToken cancellationToken = default);
}
```

> **ISP in action:** A caller that only needs to manage models can take a
> dependency on `IModelStore`. A caller that only does inference takes
> `IModelInference`. A bare-llama.cpp adapter implements `IModelInference`
> *only* and is still a first-class `IModelInference` in the system, even though
> it can't `IModelStore.PullAsync`.

### 5.5 `ModelRunnerCapabilities`

Feature detection so callers (and tests) can branch on what the backing
implementation actually supports:

```csharp
public sealed class ModelRunnerCapabilities
{
  public bool SupportsManagement { get; init; } // pull/ls/rm/...
  public bool SupportsRuntimeControl { get; init; } // ps/configure/unload/...
  public bool SupportsInference { get; init; } // chat/completion/embed
  public bool SupportsStreaming { get; init; }
  public bool SupportsEmbeddings { get; init; }
  public bool SupportsPackaging { get; init; } // package/tag/push
  public string DefaultBackend { get; init; } // "llama.cpp"
  public IReadOnlyList<string> AvailableBackends { get; init; }
}
```

---

## 6. Driver Ports (Internal Hexagonal Ports)

These live in `FluentDocker/Drivers/` next to `IVolumeDriver` etc., return
`CommandResponse<T>`, take `DriverContext` first, and are resolved per-`driverId`
through the kernel. They are the **ports**; CLI/HTTP classes are the **adapters**.

### 6.1 `IModelManagementDriver`

```csharp
namespace FluentDocker.Drivers;

public interface IModelManagementDriver
{
  Task<CommandResponse<ModelInfo>> PullAsync(DriverContext context,
      ModelReference model, IProgress<ModelPullProgress> progress = null,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<IList<ModelInfo>>> ListAsync(DriverContext context,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<ModelInfo>> InspectAsync(DriverContext context,
      ModelReference model, CancellationToken cancellationToken = default);

  Task<CommandResponse<Unit>> RemoveAsync(DriverContext context,
      ModelReference model, bool force = false,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<Unit>> TagAsync(DriverContext context,
      ModelReference source, ModelReference target,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<Unit>> PushAsync(DriverContext context,
      ModelReference model, CancellationToken cancellationToken = default);

  Task<CommandResponse<ModelInfo>> PackageAsync(DriverContext context,
      ModelPackageRequest request, CancellationToken cancellationToken = default);

  Task<CommandResponse<ModelPruneResult>> PruneAsync(DriverContext context,
      bool all = false, CancellationToken cancellationToken = default);

  Task<CommandResponse<ModelDiskUsage>> DiskUsageAsync(DriverContext context,
      CancellationToken cancellationToken = default);
}
```

### 6.2 `IModelRuntimeDriver`

```csharp
namespace FluentDocker.Drivers;

public interface IModelRuntimeDriver
{
  Task<CommandResponse<ModelRunnerStatus>> StatusAsync(DriverContext context,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<ModelRunnerVersion>> VersionAsync(DriverContext context,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<IList<RunningModel>>> ListRunningAsync(DriverContext context,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<Unit>> LoadAsync(DriverContext context,
      ModelReference model, ModelRunOptions options = null,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<Unit>> UnloadAsync(DriverContext context,
      ModelReference model, bool all = false,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<Unit>> ConfigureAsync(DriverContext context,
      ModelReference model, ModelConfigureOptions options,
      CancellationToken cancellationToken = default);

  IAsyncEnumerable<string> LogsAsync(DriverContext context, bool follow = false,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<Unit>> InstallRunnerAsync(DriverContext context,
      ModelRunnerInstallOptions options = null,
      CancellationToken cancellationToken = default);
}
```

### 6.3 `IModelInferenceDriver`

```csharp
namespace FluentDocker.Drivers;

public interface IModelInferenceDriver
{
  Task<CommandResponse<ChatCompletionResponse>> ChatCompletionAsync(
      DriverContext context, ChatCompletionRequest request,
      CancellationToken cancellationToken = default);

  IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(
      DriverContext context, ChatCompletionRequest request,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<CompletionResponse>> CompletionAsync(
      DriverContext context, CompletionRequest request,
      CancellationToken cancellationToken = default);

  IAsyncEnumerable<CompletionChunk> CompletionStreamAsync(
      DriverContext context, CompletionRequest request,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<EmbeddingsResponse>> EmbeddingsAsync(
      DriverContext context, EmbeddingsRequest request,
      CancellationToken cancellationToken = default);

  Task<CommandResponse<IList<OpenAiModel>>> ListEngineModelsAsync(
      DriverContext context, CancellationToken cancellationToken = default);
}
```

> **Streaming note:** Streaming methods return `IAsyncEnumerable<T>` directly
> (not `CommandResponse<IAsyncEnumerable<T>>`). Per-call failures surface as
> exceptions thrown during enumeration (wrapped in `ModelRunnerException`),
> because a `CommandResponse` envelope cannot represent a fault that occurs
> mid-stream. This matches the existing streaming approach in the Docker API
> `StreamDriver`.

### 6.4 Optional aggregate port

For convenience inside adapters/tests we *may* declare:

```csharp
public interface IModelDriver
  : IModelManagementDriver, IModelRuntimeDriver, IModelInferenceDriver { }
```

…but it is **not** required. The CLI pack registers
`IModelManagementDriver` + `IModelRuntimeDriver`; the API pack registers
`IModelInferenceDriver` (+ optionally management via the native socket API).
This is the crux of the ISP design: **no adapter is forced to stub a method it
cannot honor**.

---

## 7. Domain Model & DTOs

All POCOs live under `FluentDocker/Model/Models/` (management/runtime) and
`FluentDocker/Model/Models/Inference/` (OpenAI DTOs). Newtonsoft is avoided;
STJ attributes (`[JsonPropertyName]`) drive (de)serialization through
`JsonHelper`.

### 7.1 `ModelReference` (value object)

Parses and renders model identifiers across registries.

```csharp
namespace FluentDocker.Model.Models;

/// <summary>
/// Immutable reference to a model: registry (optional), namespace, name, tag.
/// Handles Docker Hub `ai/qwen3:latest`, Hugging Face `hf.co/org/repo:Q4_K_M`,
/// and fully-qualified `registry.example.com/ns/name:tag`.
/// </summary>
public sealed class ModelReference
{
  public string Registry { get; }   // null => default (Docker Hub)
  public string Namespace { get; }   // e.g. "ai", "org"
  public string Name { get; }        // e.g. "qwen3"
  public string Tag { get; }         // e.g. "latest", "Q4_K_M"
  public string Digest { get; }      // optional @sha256:...

  public bool IsHuggingFace => string.Equals(Registry, "hf.co",
      StringComparison.OrdinalIgnoreCase);

  public static ModelReference Parse(string reference);
  public static bool TryParse(string reference, out ModelReference model);

  /// <summary>Canonical CLI form, e.g. "ai/qwen3:latest" or "hf.co/org/repo".</summary>
  public override string ToString();
}
```

Parsing rules:

| Input | Registry | Namespace | Name | Tag |
|---|---|---|---|---|
| `ai/qwen3` | `null` | `ai` | `qwen3` | `latest` |
| `ai/qwen3:8b-q4` | `null` | `ai` | `qwen3` | `8b-q4` |
| `hf.co/bartowski/Llama-3.2` | `hf.co` | `bartowski` | `Llama-3.2` | `latest` |
| `registry.io/team/m:v1` | `registry.io` | `team` | `m` | `v1` |

### 7.2 Management/runtime POCOs

```csharp
public sealed class ModelInfo
{
  public string Id { get; init; }                 // image id / digest
  public ModelReference Reference { get; init; }
  public IReadOnlyList<string> Tags { get; init; }
  public string Format { get; init; }             // "gguf" | "safetensors"
  public string Architecture { get; init; }       // e.g. "llama"
  public string ParameterCount { get; init; }     // "7B"
  public string Quantization { get; init; }        // "Q4_K_M"
  public long Size { get; init; }                  // bytes
  public DateTime Created { get; init; }
  public IReadOnlyDictionary<string, string> Config { get; init; } // ctx size, etc.
}

public sealed class RunningModel
{
  public ModelReference Reference { get; init; }
  public string Backend { get; init; }            // "llama.cpp"
  public string Mode { get; init; }               // "loaded" | "running"
  public long MemoryBytes { get; init; }
  public DateTime? LastUsed { get; init; }
}

public sealed class ModelRunnerStatus
{
  public bool Running { get; init; }
  public string Backend { get; init; }
  public Uri Endpoint { get; init; }
  public string Error { get; init; }              // populated when not running
}

public sealed class ModelRunnerVersion
{
  public string CliVersion { get; init; }
  public string EngineVersion { get; init; }      // llama.cpp build
  public string ApiVersion { get; init; }
}

public sealed class ModelDiskUsage
{
  public long ModelsSizeBytes { get; init; }
  public int ModelCount { get; init; }
  public long ReclaimableBytes { get; init; }
}

public sealed class ModelPruneResult
{
  public IReadOnlyList<string> Removed { get; init; }
  public long ReclaimedBytes { get; init; }
}

public sealed class ModelPullProgress
{
  public string Status { get; init; }             // "Downloading", "Verifying"…
  public string Layer { get; init; }
  public long Current { get; init; }
  public long Total { get; init; }
  public double Fraction => Total > 0 ? (double)Current / Total : 0d;
}
```

### 7.3 Option records

```csharp
public sealed class ModelRunOptions
{
  public bool Detach { get; init; }                       // -d
  public bool IgnoreRuntimeMemoryCheck { get; init; }
  public string Backend { get; init; }                    // override engine
  public bool Debug { get; init; }
}

public sealed class ModelConfigureOptions
{
  /// <summary>`--context-size N`. Use <c>0</c>/<c>null</c> to leave unchanged,
  /// or <see cref="ResetContextSize"/> to send `--context-size -1` (reset to
  /// engine default).</summary>
  public int? ContextSize { get; init; }
  public bool ResetContextSize { get; init; }             // emits --context-size -1
  public ModelBackend Backend { get; init; }              // --backend llama.cpp|vllm|diffusers

  /// <summary>Raw flags passed verbatim AFTER the `--` separator, e.g.
  /// <c>["--temp","0.7","--top-p","0.9"]</c>. This is a passthrough to the
  /// inference engine; see <see cref="LlamaCppRuntimeFlags"/> for a typed
  /// builder that renders into this list.</summary>
  public IReadOnlyList<string> RuntimeFlags { get; init; }

  /// <summary>vLLM only — JSON passed to `--hf_overrides`
  /// (e.g. max_model_len, gpu_memory_utilization, tensor_parallel_size).</summary>
  public string HfOverridesJson { get; init; }
}

public sealed class ModelPackageRequest
{
  public string GgufPath { get; init; }                   // --gguf
  public ModelReference Target { get; init; }             // repository:tag
  public bool Push { get; init; }                         // --push
  public IReadOnlyDictionary<string, string> Labels { get; init; }
  public string Licaense { get; init; }
}

public sealed class ModelRunnerInstallOptions
{
  public string Gpu { get; init; }                        // "auto" | "cuda" | "none"
}

public sealed class ModelRunnerUninstallOptions
{
  public bool RemoveImages { get; init; }                 // --images
  public bool RemoveModels { get; init; }                 // --models (also delete local models)
}
```

> **`ModelBackend`** is a small extensible value (not a closed enum) so unknown
> future backends don't break callers:
>
> ```csharp
> public readonly struct ModelBackend
> {
>   public string Name { get; }                 // "llama.cpp" | "vllm" | "diffusers" | custom
>   public static readonly ModelBackend LlamaCpp = new("llama.cpp");
>   public static readonly ModelBackend Vllm     = new("vllm");
>   public static readonly ModelBackend Diffusers= new("diffusers");
>   public static ModelBackend Custom(string name) => new(name);
>   public bool IsDefault => Name is null;      // "leave unset"
> }
> ```

### 7.4 Inference DTOs (OpenAI-compatible)

Split across multiple files to respect the 500-line rule:
`Model/Models/Inference/Chat*.cs`, `Completion*.cs`, `Embeddings*.cs`, `Common*.cs`.

```csharp
namespace FluentDocker.Model.Models.Inference;

public sealed class ChatCompletionRequest
{
  [JsonPropertyName("model")] public string Model { get; set; }
  [JsonPropertyName("messages")] public IList<ChatMessage> Messages { get; set; }
  [JsonPropertyName("max_tokens")] public int? MaxTokens { get; set; }
  [JsonPropertyName("temperature")] public double? Temperature { get; set; }
  [JsonPropertyName("top_p")] public double? TopP { get; set; }
  [JsonPropertyName("stream")] public bool? Stream { get; set; }
  [JsonPropertyName("stop")] public IList<string> Stop { get; set; }
  [JsonPropertyName("presence_penalty")] public double? PresencePenalty { get; set; }
  [JsonPropertyName("frequency_penalty")] public double? FrequencyPenalty { get; set; }
  [JsonPropertyName("seed")] public int? Seed { get; set; }
  // tools / response_format reserved for future use (see §22)
}

public sealed class ChatMessage
{
  [JsonPropertyName("role")] public string Role { get; set; }     // system|user|assistant|tool
  [JsonPropertyName("content")] public string Content { get; set; }
  [JsonPropertyName("name")] public string Name { get; set; }
}

public sealed class ChatCompletionResponse
{
  [JsonPropertyName("id")] public string Id { get; set; }
  [JsonPropertyName("object")] public string Object { get; set; }
  [JsonPropertyName("created")] public long Created { get; set; }
  [JsonPropertyName("model")] public string Model { get; set; }
  [JsonPropertyName("choices")] public IList<ChatChoice> Choices { get; set; }
  [JsonPropertyName("usage")] public Usage Usage { get; set; }
}

public sealed class ChatChoice
{
  [JsonPropertyName("index")] public int Index { get; set; }
  [JsonPropertyName("message")] public ChatMessage Message { get; set; }
  [JsonPropertyName("finish_reason")] public string FinishReason { get; set; }
}

public sealed class ChatCompletionChunk      // streaming delta (SSE)
{
  [JsonPropertyName("id")] public string Id { get; set; }
  [JsonPropertyName("choices")] public IList<ChatChunkChoice> Choices { get; set; }
}

public sealed class ChatChunkChoice
{
  [JsonPropertyName("index")] public int Index { get; set; }
  [JsonPropertyName("delta")] public ChatMessage Delta { get; set; }
  [JsonPropertyName("finish_reason")] public string FinishReason { get; set; }
}

public sealed class Usage
{
  [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; set; }
  [JsonPropertyName("completion_tokens")] public int CompletionTokens { get; set; }
  [JsonPropertyName("total_tokens")] public int TotalTokens { get; set; }
}

public sealed class EmbeddingsRequest
{
  [JsonPropertyName("model")] public string Model { get; set; }
  [JsonPropertyName("input")] public IList<string> Input { get; set; }
}

public sealed class EmbeddingsResponse
{
  [JsonPropertyName("data")] public IList<EmbeddingData> Data { get; set; }
  [JsonPropertyName("model")] public string Model { get; set; }
  [JsonPropertyName("usage")] public Usage Usage { get; set; }
}

public sealed class EmbeddingData
{
  [JsonPropertyName("index")] public int Index { get; set; }
  [JsonPropertyName("embedding")] public IList<float> Embedding { get; set; }
}

public sealed class OpenAiModel
{
  [JsonPropertyName("id")] public string Id { get; set; }
  [JsonPropertyName("object")] public string Object { get; set; }
  [JsonPropertyName("owned_by")] public string OwnedBy { get; set; }
}
```

(`CompletionRequest/Response/Chunk` mirror chat but use `prompt`/`text`.)

### 7.5 `LlamaCppRuntimeFlags` — typed convenience over the passthrough

DMR passes runtime flags **verbatim** to the engine after a `--` separator
(`docker model configure ai/x -- --temp 0.7 --top-p 0.9`). We therefore keep the
raw `RuntimeFlags` list as the source of truth, but offer a typed *builder* that
renders into it, so callers don't fat-finger flag names while power users keep
full passthrough freedom:

```csharp
namespace FluentDocker.Model.Models.Options;

public sealed class LlamaCppRuntimeFlags
{
  // Sampling
  public double? Temperature { get; init; }       // --temp        (default 0.8)
  public int? TopK { get; init; }                 // --top-k       (default 40)
  public double? TopP { get; init; }              // --top-p       (default 0.9)
  public double? MinP { get; init; }              // --min-p       (default 0.05)
  public double? RepeatPenalty { get; init; }     // --repeat-penalty (default 1.1)

  // Performance
  public int? Threads { get; init; }              // --threads
  public int? ThreadsBatch { get; init; }         // --threads-batch
  public int? BatchSize { get; init; }            // --batch-size  (default 512)
  public bool? Mlock { get; init; }               // --mlock
  public bool? NoMmap { get; init; }              // --no-mmap

  // GPU
  public int? GpuLayers { get; init; }            // --n-gpu-layers (default: all if available)
  public int? MainGpu { get; init; }              // --main-gpu     (default 0)
  public string SplitMode { get; init; }          // --split-mode   none|layer|row

  // Advanced
  public double? RopeFreqBase { get; init; }      // --rope-freq-base
  public double? RopeFreqScale { get; init; }     // --rope-freq-scale
  public string RopeScaling { get; init; }        // --rope-scaling
  public bool? NoPrefillAssistant { get; init; }  // --no-prefill-assistant
  public int? ReasoningBudget { get; init; }      // --reasoning-budget (default 0)

  /// <summary>Escape hatch: extra raw flags appended verbatim.</summary>
  public IReadOnlyList<string> Raw { get; init; }

  /// <summary>Render to the flat token list DMR expects after `--`.</summary>
  public IReadOnlyList<string> ToArgs();
}
```

See [Appendix E](#appendix-e-full-configuration-reference-flags-defaults-env-vars)
for the exhaustive flag/default table. The builder layer (§13) exposes
`WithRuntimeFlags(Action<LlamaCppRuntimeFlagsBuilder>)` *and* the raw
`WithRuntimeFlags(params string[])` for full passthrough.

---

## 8. The CLI Driver (Docker `model` plugin)

Two adapter classes, both extending `DockerCliDriverBase` (reusing
`ExecuteCommandAsync`, `QuoteArgumentIfNeeded`, sudo handling, binary
resolution):

* `DockerCliModelManagementDriver : DockerCliDriverBase, IModelManagementDriver`
* `DockerCliModelRuntimeDriver : DockerCliDriverBase, IModelRuntimeDriver`

> Split into two classes (not one) both to honor ISP at the adapter level and to
> keep each file under 500 lines.

### 8.1 Command mapping

| Port method | `docker model …` command | Notes |
|---|---|---|
| `PullAsync` | `model pull <ref>` | Stream stdout lines → `ModelPullProgress`. |
| `ListAsync` | `model ls --json` | Parse JSON array → `ModelInfo[]`. |
| `InspectAsync` | `model inspect <ref> --json` | Parse single object. Fallback `--format`. |
| `RemoveAsync` | `model rm [-f] <ref>` | `-f` when `force`. |
| `TagAsync` | `model tag <src> <dst>` | |
| `PushAsync` | `model push <ref>` | |
| `PackageAsync` | `model package --gguf <path> [--push] <repo:tag>` | |
| `PruneAsync` | `model prune [-a]` / `model system prune` | |
| `DiskUsageAsync` | `model df` / `model system df` | Parse table or `--json`. |
| `StatusAsync` | `model status` | Exit code + text → running bool. |
| `VersionAsync` | `model version` | Parse versions. |
| `ListRunningAsync` | `model ps --json` | |
| `LoadAsync` | `model run -d [--ignore-runtime-memory-check] <ref>` | Detached load. |
| `UnloadAsync` | `model unload <ref>` / `model unload --all` | |
| `ConfigureAsync` | `model configure [--context-size N] [--backend B] <ref> [-- <runtime flags…>]` | Runtime flags after `--`; `--context-size -1` resets; `--hf_overrides '<json>'` for vLLM. |
| `LogsAsync` | `model logs [-f]` | Stream lines. |
| `InstallRunnerAsync` | `model install-runner [--gpu auto\|cuda\|none]` | Engine CE only. |
| `UninstallRunnerAsync` | `model uninstall-runner [--images] [--models]` | Engine CE only; used for upgrades. |

### 8.2 Example adapter method

```csharp
public async Task<CommandResponse<IList<ModelInfo>>> ListAsync(
    DriverContext context, CancellationToken cancellationToken = default)
{
  try
  {
    var result = await ExecuteCommandAsync("model ls --json", cancellationToken)
        .ConfigureAwait(false);

    if (!result.Success)
      return CommandResponse<IList<ModelInfo>>.Fail(
          result.Error ?? "model ls failed",
          ErrorCodes.Model.ListFailed,
          CreateErrorContext(context, "ListModels", result),
          result.ExitCode);

    var items = ModelJsonParser.ParseList(result.Output); // STJ via JsonHelper
    return CommandResponse<IList<ModelInfo>>.Ok(items);
  }
  catch (Exception ex)
  {
    return CommandResponse<IList<ModelInfo>>.Fail(ex.Message, ErrorCodes.Model.ListFailed);
  }
}
```

### 8.3 Output parsing

* `--json` is used wherever supported; a `ModelJsonParser` static helper (in
  `Drivers/Docker/Cli/Components/Parsing/`) maps DMR's JSON onto our POCOs using
  `JsonHelper.CaseInsensitiveOptions`.
* For commands without `--json` (older plugin versions), a table parser is
  provided as a fallback, guarded by version detection (`VersionAsync`).
* `pull` and `logs` are **line-streamed**: stdout is read incrementally and each
  line is parsed into a progress event. This requires a streaming variant of
  `ExecuteCommandAsync` — see §8.5.

### 8.4 No CLI inference — inference is HTTP-only

`docker model run <ref> "prompt"` produces only a blocking one-shot completion to
stdout: it **cannot stream tokens and cannot embed**, so it cannot honor the
`IModelInferenceDriver` contract (which exposes `IAsyncEnumerable` streaming and an
embeddings call). Rather than register a *degraded* CLI inference adapter and leak
that limitation into capabilities, the design treats inference as **HTTP-only**:

* `DockerCliDriverPack` satisfies `IModelInferenceDriver` by composing the HTTP
  adapter — a `DockerApiModelInferenceDriver` over a `ModelApiConnection` bound to
  `ModelRunnerEndpoint.Default()` — and registering it under the port. The pack
  **owns** that connection: it is `IAsyncDisposable`, and the
  kernel → `DriverRegistry.DisposeAsync` chain releases it.
* A caller asks for the `IModelInferenceDriver` port and gets a full, streaming-capable implementation;
  *how* it is fulfilled (HTTP, here) is invisible, and `SupportsStreaming` /
  `SupportsEmbeddings` are therefore always true whenever an inference port exists.

### 8.5 Streaming command execution

`DockerCliDriverBase` currently buffers stdout fully. We add a protected
`ExecuteStreamingCommandAsync(string args, CancellationToken)` returning
`IAsyncEnumerable<string>` (one item per stdout line), reusing the existing
process-spawn/argument-quoting/sudo logic but yielding lines as they arrive.
This is additive and benefits `logs`/`pull` without altering existing behavior.

---

## 9. The HTTP/API Driver (OpenAI-compatible endpoint)

`DockerApiModelInferenceDriver : IModelInferenceDriver` lives under
`Drivers/Docker/Api/Components/`. It does **not** extend `DockerApiDriverBase`
(which is bound to the Docker Engine socket + Docker response envelope). DMR's
inference endpoint speaks **raw OpenAI JSON**, not Docker's wrapper, so it uses a
dedicated, thinner connection abstraction (§10).

### 9.1 Endpoint mapping

| Port method | HTTP | Path |
|---|---|---|
| `ChatCompletionAsync` | POST | `/engines/{engine}/v1/chat/completions` |
| `ChatCompletionStreamAsync` | POST (SSE) | `/engines/{engine}/v1/chat/completions` (`stream=true`) |
| `CompletionAsync` | POST | `/engines/{engine}/v1/completions` |
| `CompletionStreamAsync` | POST (SSE) | `/engines/{engine}/v1/completions` (`stream=true`) |
| `EmbeddingsAsync` | POST | `/engines/{engine}/v1/embeddings` |
| `ListEngineModelsAsync` | GET | `/engines/{engine}/v1/models` |

`{engine}` defaults to `llama.cpp`; it can be omitted (`/engines/v1/...`) — both
are supported and configurable via `ModelRunnerEndpoint`.

### 9.2 Native management over the API (optional second adapter)

`DockerApiModelManagementDriver : IModelManagementDriver` can implement
management via the native socket endpoints, giving an HTTP path that doesn't
need the `docker` binary on PATH:

| Port method | HTTP | Path |
|---|---|---|
| `PullAsync` | POST | `/models/create` (`{"from": "ai/qwen3"}`) — streamed progress |
| `ListAsync` | GET | `/models` |
| `InspectAsync` | GET | `/models/{namespace}/{name}` |
| `RemoveAsync` | DELETE | `/models/{namespace}/{name}` |

`Tag/Push/Package/Prune/DiskUsage` have no native endpoints and remain
CLI-only; the API management adapter reports them via `NotSupportedException`
(capabilities reflect this). This is fine — the composite runner prefers
whichever adapter the kernel resolves and degrades gracefully.

### 9.3 Example endpoint method

```csharp
public async Task<CommandResponse<ChatCompletionResponse>> ChatCompletionAsync(
    DriverContext context, ChatCompletionRequest request,
    CancellationToken cancellationToken = default)
{
  request.Stream = false;
  var json = JsonSerializer.Serialize(request, JsonHelper.DefaultOptions);
  using var content = new StringContent(json, Encoding.UTF8, "application/json");

  using var resp = await _connection.PostAsync(
      $"{_engineBase}/v1/chat/completions", content, cancellationToken)
      .ConfigureAwait(false);

  if (!resp.IsSuccessStatusCode)
    return CommandResponse<ChatCompletionResponse>.Fail(
        await SafeReadError(resp, cancellationToken),
        ErrorCodes.ModelInference.RequestFailed,
        context: CreateApiErrorContext(context, "ChatCompletion", resp),
        exitCode: (int)resp.StatusCode);

  var body = await resp.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
  var dto = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(
      body, JsonHelper.CaseInsensitiveOptions, cancellationToken).ConfigureAwait(false);

  return CommandResponse<ChatCompletionResponse>.Ok(dto);
}
```

---

## 10. Inference Connection & Endpoint Resolution

### 10.1 `IModelApiConnection`

A thin HTTP abstraction (parallel to `IDockerApiConnection` but **not** Docker-
versioned and **not** Docker-response-wrapped):

```csharp
namespace FluentDocker.Drivers.Models.Connection;

public interface IModelApiConnection : IAsyncDisposable
{
  Uri BaseAddress { get; }
  Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default);
  Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken ct = default);
  Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct = default);
  Task<Stream> PostStreamAsync(string path, HttpContent content, CancellationToken ct = default);
  Task<bool> PingAsync(CancellationToken ct = default);
}
```

Default implementation `ModelApiConnection` wraps an `HttpClient` configured per
the resolved endpoint (TCP, container DNS, or unix socket via
`SocketsHttpHandler.ConnectCallback`, reusing the technique already present in
`DockerApiConnection`).

### 10.2 `ModelRunnerEndpoint` (value object)

```csharp
namespace FluentDocker.Model.Models;

public sealed class ModelRunnerEndpoint
{
  public Uri BaseAddress { get; }
  public string Engine { get; }            // "llama.cpp" (path segment)
  public string UnixSocketPath { get; }    // non-null => connect via socket
  public bool IncludeEngineInPath { get; } // /engines/llama.cpp/v1 vs /engines/v1

  // Factories:
  public static ModelRunnerEndpoint HostTcp(int port = 12434, string engine = "llama.cpp");
  public static ModelRunnerEndpoint ContainerInternal(int port = 12434, string engine = "llama.cpp");
  public static ModelRunnerEndpoint UnixSocket(string socketPath = null, string engine = "llama.cpp");
  public static ModelRunnerEndpoint Custom(Uri baseAddress, string engine = "llama.cpp");
}
```

Default resolution order (when the user doesn't specify):

1. `DOCKER_MODEL_RUNNER_URL` env var, if set.
2. `http://localhost:12434` (host TCP) — probe `PingAsync`.
3. `http://model-runner.docker.internal:12434` (when running inside a container).
4. Unix socket `$HOME/.docker/run/docker.sock`.

`ModelRunnerEndpoint.Default()` implements the **synchronous, no-I/O** prefix
(step 1 → step 2: `DOCKER_MODEL_RUNNER_URL` else host TCP) and is the single
source of truth for the default endpoint used by the CLI pack and the runner
builder. Steps 3–4 are selected explicitly via the named factories
(`ContainerInternal()`, `UnixSocket()`); the builder exposes overrides for all of
the above (§13).

---

## 11. Streaming (SSE) Design

DMR streams chat/completions as **Server-Sent Events**: lines prefixed `data: `,
terminated by `data: [DONE]`. The streaming path:

```csharp
public async IAsyncEnumerable<ChatCompletionChunk> ChatCompletionStreamAsync(
    DriverContext context, ChatCompletionRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
  request.Stream = true;
  var json = JsonSerializer.Serialize(request, JsonHelper.DefaultOptions);
  using var content = new StringContent(json, Encoding.UTF8, "application/json");

  await using var stream = await _connection.PostStreamAsync(
      $"{_engineBase}/v1/chat/completions", content, cancellationToken)
      .ConfigureAwait(false);
  using var reader = new StreamReader(stream, Encoding.UTF8);

  string line;
  while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) != null)
  {
    if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
      continue;

    var payload = line.AsSpan(5).Trim();
    if (payload.SequenceEqual("[DONE]"))
      yield break;

    ChatCompletionChunk chunk;
    try
    {
      chunk = JsonSerializer.Deserialize<ChatCompletionChunk>(payload, JsonHelper.CaseInsensitiveOptions);
    }
    catch (JsonException ex)
    {
      throw new ModelRunnerException("Malformed SSE chunk", ErrorCodes.ModelInference.StreamParseError, ex);
    }
    yield return chunk;
  }
}
```

* Reuses STJ via `JsonHelper`.
* Honors `CancellationToken` per read.
* Mid-stream faults throw `ModelRunnerException` (cannot use `CommandResponse`).
* The public `IModelRunner.ChatStreamAsync` projects chunks → `delta.content`
  strings for ergonomics.

---

## 12. Services & Lifecycle

### 12.1 `ModelRunnerService` — the `IModelRunner` implementation

`Services/Impl/ModelRunnerService.cs` (split with partials:
`.Store.cs`, `.Engine.cs`, `.Inference.cs`) implements `IModelRunner`. It:

* Resolves ports lazily via `kernel.SysCtl<IModelManagementDriver>(driverId)`,
  `…<IModelRuntimeDriver>…`, `…<IModelInferenceDriver>…`, catching
  `InterfaceNotSupportedException` to compute `Capabilities`.
* Translates `CommandResponse<T>` failures into `ModelRunnerException`
  (subclass of `DriverException`).
* Holds the resolved `ModelRunnerEndpoint` and default `ModelReference`.
* Implements `IAsyncDisposable` to dispose the `IModelApiConnection`.

### 12.2 `IModelService` — single-model lifecycle (optional)

For parity with `IVolumeService`/`IContainerService` (a handle to one resource),
we add `IModelService : IServiceAsync` representing a **single loaded model**:

```csharp
public interface IModelService : IServiceAsync
{
  ModelReference Model { get; }
  Task<ModelInfo> InspectAsync(CancellationToken cancellationToken = default);
  Task ConfigureAsync(ModelConfigureOptions options, CancellationToken cancellationToken = default);
  IModelRunner Runner { get; }   // inference bound to this model
}
```

* `StartAsync` → `IModelRuntimeDriver.LoadAsync` (pull-if-absent + run -d).
* `StopAsync` → `IModelRuntimeDriver.UnloadAsync`.
* `RemoveAsync` → `IModelManagementDriver.RemoveAsync`.
* `State` transitions via the standard `ServiceRunningState` machine, so model
  services participate in the same hook/observer pipeline as containers.

This lets users write `using var model = builder…ForModel("ai/qwen3").Build();`
and have the model auto-unloaded on dispose — mirroring container ergonomics.

---

## 13. Builders & Fluent API

### 13.1 Entry points

```csharp
// Direct runner (inference-first):
var runner = new Builder()
    .WithinDriver("docker")
    .UseModelRunner()                       // → IModelRunnerBuilder
    .ForModel("ai/qwen3")
    .WithContextSize(8192)
    .WithEndpoint(ModelRunnerEndpoint.HostTcp())   // optional
    .PullIfMissing()                        // optional
    .Build();                               // → IModelRunner

var answer = await runner.ChatAsync("Hello");

// Managed single-model service (lifecycle-first):
await using var model = new Builder()
    .WithinDriver("docker")
    .UseModel("ai/qwen3")                   // → IModelServiceBuilder
    .WithContextSize(8192)
    .KeepRunning(false)                     // unload on dispose
    .Build();                               // → IModelService
await model.StartAsync();
var reply = await model.Runner.ChatAsync("Hi");
```

### 13.2 Builder interfaces

```csharp
namespace FluentDocker.Builders;

public interface IModelRunnerBuilder : IDriverScopedBuilder
{
  IModelRunnerBuilder ForModel(string reference);
  IModelRunnerBuilder ForModel(ModelReference reference);
  IModelRunnerBuilder WithContextSize(int tokens);
  IModelRunnerBuilder WithBackend(string backend);
  IModelRunnerBuilder WithRuntimeFlags(params string[] flags);
  IModelRunnerBuilder WithEndpoint(ModelRunnerEndpoint endpoint);
  IModelRunnerBuilder WithInferenceDriver(IModelInferenceDriver inference); // §13.6
  IModelRunnerBuilder WithInferenceDriver(string driverId);                 // §13.6
  IModelRunnerBuilder PullIfMissing(bool pull = true);
  IModelRunner Build();
  Task<IModelRunner> BuildAsync(CancellationToken cancellationToken = default);
}

public interface IModelServiceBuilder : IDriverScopedBuilder
{
  IModelServiceBuilder WithContextSize(int tokens);
  IModelServiceBuilder WithBackend(string backend);
  IModelServiceBuilder WithRunOptions(Action<ModelRunOptionsBuilder> configure);
  IModelServiceBuilder KeepRunning(bool keep = true);
  IModelServiceBuilder PullIfMissing(bool pull = true);
  IModelService Build();
}
```

There is intentionally **no transport selector** — inference is always the full
HTTP data plane (§8.4). `WithEndpoint(...)` points inference at a non-default
address (a remote runner, a container-internal alias, or any OpenAI-compatible
server) while management/runtime stay on the scoped driver; see §13.6 for
splitting management and inference across endpoints or drivers.

### 13.3 Driver-scoped extension methods

Following the `TryDriver<T>()` / `RequireDriver<T>()` pattern in
`DriverScopedBuilderExtensions`:

```csharp
public static class ModelDriverScopedBuilderExtensions
{
  public static bool TryUseModelRunner(this IDriverScopedBuilder b, out IModelRunnerBuilder builder);
  public static IModelRunnerBuilder UseModelRunner(this IDriverScopedBuilder b);
  public static IModelServiceBuilder UseModel(this IDriverScopedBuilder b, string reference);
}
```

`UseModelRunner` calls `RequireDriver<IModelInferenceDriver>()` (or management,
depending on intent) under the hood; `TryUseModelRunner` returns `false` when the
resolved driver pack lacks model support (e.g. a Podman pack without RamaLama).

### 13.4 Top-level `Builder` convenience

`Builder` gains `UseModelRunner()` / `UseModel(string)` shortcuts that assume the
current scope (set by `WithinDriver`), consistent with existing
`UseContainer`/`UseVolume` shortcuts.

### 13.5 Wiring a model into a container (the consumption seam)

This is the imperative counterpart to the Compose `models:` binding (§22): make
an existing `ContainerBuilder` consume a model endpoint. It is an **additive
extension on the container builder** — it does *not* give the model a network or
a volume (it has neither, §4.2); it injects the endpoint as environment.

```csharp
public static class ContainerModelBuilderExtensions
{
  /// <summary>
  /// Inject a model endpoint into the container's environment and ensure the
  /// container can reach it. Mirrors what Compose's `models:` binding does
  /// (LLM_URL / LLM_MODEL), but imperatively.
  /// </summary>
  public static IContainerBuilder WithModel(
      this IContainerBuilder builder,
      ModelReference model,
      ModelRunnerEndpoint endpoint = null,     // default: container-internal DNS
      string endpointVar = "LLM_URL",
      string modelVar = "LLM_MODEL");
}
```

Behavior:

* **Endpoint default = container-internal**, *not* host TCP: when a container is
  the consumer, the correct base is `model-runner.docker.internal:12434`
  (Desktop) or the bridge gateway `172.17.0.1:12434` (Engine) — never
  `localhost`, which would resolve to the container itself. The helper picks the
  right one from the active `driverId`/runtime; `localhost` is only the default
  for **host-process** consumers.
* **Reachability:** on Desktop the internal DNS name resolves automatically; on
  Engine the helper adds `--add-host model-runner.docker.internal:host-gateway`
  (or injects the gateway IP) so the same code works on both.
* **No new network/volume is created.** The model is reached over the existing
  bridge; this is pure env + host-alias wiring.

```csharp
var stack = new Builder()
  .WithinDriver("docker")
  .UseContainer().UseImage("my-app:latest")
    .WithModel("ai/qwen3")          // injects LLM_URL=http://model-runner.docker.internal:12434/engines/v1
                                    // and LLM_MODEL=ai/qwen3
  .Builder()
  .Build();
```

This keeps the integration honest: a container has networks and volumes; a model
has an endpoint; the helper connects the two without pretending the model is a
network peer.

### 13.6 Splitting management and inference across endpoints/drivers

Because management/runtime and inference are **independent ports**, a runner can
pair a control plane resolved from one driver with an inference plane that lives
somewhere else entirely. The three ports are resolved separately, so "create/load
a model with driver A, run inference on it via driver/endpoint B" is a first-class
composition — not a special case.

**(a) Same runner, non-default inference address — `WithEndpoint`.** Management and
runtime resolve from the scoped driver; only inference is repointed. Typical when
code inside a container manages via the socket but must reach the engine by its
container-internal alias:

```csharp
await using var runner = await new Builder().WithinDriver("docker", kernel)
    .UseModelRunner()
    .ForModel("ai/qwen3")
    .PullIfMissing()                                   // pull/load via the local docker CLI
    .WithEndpoint(ModelRunnerEndpoint.ContainerInternal())  // infer via model-runner.docker.internal:12434
    .BuildAsync();
```

**(b) Control plane here, inference on a different server — two runners.** Manage on
the local DMR through the CLI pack; do inference on a *different* OpenAI-compatible
engine (a remote DMR, vLLM, LM Studio, a hosted endpoint) via an inference-only
runner. They share only the model id:

```csharp
// Control plane: pull / load / configure on the local runner (full management).
await using var control = await new Builder().WithinDriver("docker", kernel)
    .UseModelRunner().ForModel("ai/qwen3").BuildAsync();
await control.LoadAsync(ModelReference.Parse("ai/qwen3"));

// Data plane: inference on any OpenAI-compatible endpoint (no management).
await using var inference = ModelRunnerEnvironment.FromVariables("LLM_URL", "LLM_MODEL");
var reply = await inference.ChatAsync("Summarise the build log");
```

**(c) Lowest level — inject any `IModelInferenceDriver` into the kernel-backed
runner.** `ModelRunnerService` resolves management/runtime from `driverId` but uses
an injected inference port verbatim. This is the seam the builder's `WithEndpoint`
uses internally, exposed for arbitrary adapters:

```csharp
var endpoint = ModelRunnerEndpoint.Custom(new Uri("http://gpu-box:12434"));
var conn     = new ModelApiConnection(endpoint);
var infer    = new DockerApiModelInferenceDriver(conn, endpoint); // any IModelInferenceDriver

await using var runner = new ModelRunnerService(
    kernel, driverId: "docker", endpoint: endpoint,
    defaultModel: ModelReference.Parse("ai/qwen3"),
    inferenceOverride: infer, ownedResource: conn);
// management/runtime → the "docker" pack; inference → the injected driver
```

**(d) Fluent shortcut — `WithInferenceDriver` (recommended).** Patterns (b)/(c) are
also a single fluent call: supply an explicit `IModelInferenceDriver`, or name
*another driver registered in the same kernel* and let the builder resolve its
inference port (`kernel.SysCtl<IModelInferenceDriver>(driverId)`) at build time.
Management/runtime stay on the scoped driver; the supplied/resolved inference plane
is owned by the caller (the runner never disposes it), and this takes precedence
over `WithEndpoint` (which only repoints the auto-built HTTP connection):

```csharp
await using var runner = await new Builder().WithinDriver("docker", kernel)  // manage here
    .UseModelRunner().ForModel("ai/qwen3")
    .WithInferenceDriver("remote-dmr")          // infer via another registered driver…
    // .WithInferenceDriver(myInferenceDriver)  // …or an explicit IModelInferenceDriver
    .BuildAsync();
```

This makes "create/load with driver A, infer with driver B" a first-class one-liner
without dropping to the `ModelRunnerService` constructor.

---

## 14. Kernel Registration, Resolution & Capabilities

### 14.1 Registration in driver packs

`DockerCliDriverPack.InitializeAsync` adds:

```csharp
_modelManagementDriver = new DockerCliModelManagementDriver(_binaryResolver);
_modelRuntimeDriver    = new DockerCliModelRuntimeDriver(_binaryResolver);
// Inference is HTTP-only: the CLI pack composes the HTTP adapter and OWNS the
// connection (the pack is IAsyncDisposable; the registry disposes it on shutdown).
var inferenceEndpoint  = ModelRunnerEndpoint.Default();  // DOCKER_MODEL_RUNNER_URL → host TCP
_modelInferenceConn    = new ModelApiConnection(inferenceEndpoint, loggerFactory: context.LoggerFactory);
_modelInferenceDriver  = new DockerApiModelInferenceDriver(_modelInferenceConn, inferenceEndpoint);

_drivers[typeof(IModelManagementDriver)] = _modelManagementDriver;
_drivers[typeof(IModelRuntimeDriver)]    = _modelRuntimeDriver;
_drivers[typeof(IModelInferenceDriver)]  = _modelInferenceDriver;
```

`DockerApiDriverPack.InitializeAsync` adds:

```csharp
_modelInferenceDriver  = new DockerApiModelInferenceDriver(modelConnection, endpoint);
_modelManagementApiDrv = new DockerApiModelManagementDriver(modelConnection);

_drivers[typeof(IModelInferenceDriver)]  = _modelInferenceDriver;
_drivers[typeof(IModelManagementDriver)] = _modelManagementApiDrv; // if not already CLI-provided
```

> **Composite resolution:** Because a single `driverId` (e.g. `"docker"`) may be
> backed by a pack that registers *both* CLI management and API inference, the
> `ModelRunnerService` resolves each port independently. When the same logical
> driver wants CLI for management and HTTP for inference, a **composite driver
> pack** (`DockerHybridDriverPack`, or the existing pack registering interfaces
> from both adapter families) supplies both. See §14.3.

### 14.2 Capabilities flag

Extend `DriverCapabilities` with:

```csharp
public bool SupportsModels { get; init; }       // any model port present
public bool SupportsModelInference { get; init; } // inference port present
```

`DockerCliDriverPack.GetCapabilitiesAsync` → `SupportsModels = true`.
`PodmanCliDriverPack.GetCapabilitiesAsync` → `SupportsModels = false` (until a
RamaLama pack is added — §16).

### 14.3 Hybrid wiring

The cleanest user experience is `WithinDriver("docker")` resolving management via
CLI and inference via HTTP transparently. Two options:

* **Option A (recommended): a single Docker pack registers all three ports**,
  using the CLI adapters for management/runtime and the API adapter for
  inference, constructing the `IModelApiConnection` from a default endpoint at
  pack-init time. This keeps `driverId="docker"` self-contained.
* **Option B: separate `driverId`s** (`"docker"` for CLI, `"docker-api"` for
  HTTP) and the `ModelRunnerService` composes across two kernels. More flexible,
  more ceremony.

We choose **Option A** for the default builder path, exposing Option B for
advanced scenarios (custom endpoints / remote runners).

---

## 15. Error Handling & Error Codes

### 15.1 New error-code groups

Add to `Model/Drivers/ErrorCodes.cs`:

```csharp
public static class Model
{
  public const string NotFound       = "MDL_001";
  public const string PullFailed     = "MDL_002";
  public const string RunFailed      = "MDL_003";
  public const string ListFailed     = "MDL_004";
  public const string ConfigureFailed= "MDL_005";
  public const string RemoveFailed   = "MDL_006";
  public const string InspectFailed  = "MDL_007";
  public const string TagFailed      = "MDL_008";
  public const string PushFailed     = "MDL_009";
  public const string PackageFailed  = "MDL_010";
  public const string PruneFailed    = "MDL_011";
  public const string LoadFailed     = "MDL_012";
  public const string UnloadFailed   = "MDL_013";
  public const string RunnerNotRunning = "MDL_014";
  public const string RunnerNotInstalled = "MDL_015";
  public const string InvalidReference = "MDL_016";
}

public static class ModelInference
{
  public const string RequestFailed   = "MIN_001";
  public const string StreamParseError= "MIN_002";
  public const string EndpointUnreachable = "MIN_003";
  public const string ModelNotLoaded  = "MIN_004";
  public const string Unauthorized    = "MIN_401";
}
```

### 15.2 `ModelRunnerException`

```csharp
public class ModelRunnerException : DriverException
{
  public ModelRunnerException(string message, string errorCode = null,
      Exception inner = null) : base(message, errorCode, inner) { }
}
```

Thrown by the service layer when a driver `CommandResponse` fails, and by
streaming methods on mid-stream faults. Includes the `ErrorContext`
(driver, operation, exit code, stderr) populated by the driver.

### 15.3 Failure-mode matrix

| Failure | Detection | Surfaced as |
|---|---|---|
| Runner not installed | `model status` exit ≠ 0 / connection refused | `ModelRunnerException(RunnerNotInstalled)` with remediation hint |
| Runner installed, not running | `StatusAsync().Running == false` | `ModelRunnerException(RunnerNotRunning)` |
| TCP endpoint disabled | `PingAsync` fails / connection refused | `ModelRunnerException(EndpointUnreachable)` on inference calls (inference is HTTP-only); management/runtime keep working over the CLI |
| Model not pulled | inference 404 / `model not found` | `ModelRunnerException(ModelNotLoaded)`; auto-pull if `PullIfMissing` |
| OOM on load | `model run` stderr / non-zero exit | `ModelRunnerException(LoadFailed)`; hint `IgnoreRuntimeMemoryCheck` |
| Malformed SSE | JSON parse during stream | `ModelRunnerException(StreamParseError)` mid-enumeration |

---

## 16. Other Container Engines (Podman / RamaLama / Generic OpenAI)

The whole point of the port split is engine pluggability. Concretely:

### 16.1 Podman

Podman has **no built-in `podman model`** equivalent. The community tool
**RamaLama** (`ramalama`) fills the role: it pulls OCI/HF models and serves an
OpenAI-compatible endpoint via `ramalama serve`. Plan:

* `RamaLamaCliModelManagementDriver` / `RamaLamaCliModelRuntimeDriver`
  (`Drivers/Podman/RamaLama/Cli/Components/`) mapping:
  * `ramalama pull <ref>`, `ramalama list --json`, `ramalama rm`,
    `ramalama inspect`, `ramalama serve <ref>` (load), `ramalama stop`.
* Reuse `DockerApiModelInferenceDriver` against the RamaLama `serve` endpoint
  (it's OpenAI-compatible) — only the `ModelRunnerEndpoint` differs.
* `PodmanRamaLamaDriverPack` registers the model ports and sets
  `SupportsModels = true`.

This is **future work** (a separate task block in TASKS.md), but the v1 design
must not preclude it — which the segregation guarantees.

### 16.2 Generic OpenAI-compatible endpoints

Because the inference path is just `IModelApiConnection` + OpenAI DTOs, a
**`GenericOpenAiModelRunner`** can target *any* OpenAI-compatible server (a bare
`llama-server`, vLLM, LM Studio, even a hosted endpoint) by supplying
`ModelRunnerEndpoint.Custom(uri)` and an API key header. It implements
`IModelInference` only (`SupportsManagement=false`). This realizes the user's
stated benefit: "a local model behind the same interface as a Bedrock/hosted
call."

### 16.3 Anthropic / Ollama dialects

DMR also serves Anthropic (`/anthropic/v1/messages`) and Ollama
(`/api/chat`) dialects. These are **not** in the v1 surface but can be added as
alternative `IModelInferenceDriver` adapters (`DockerApiAnthropicInferenceDriver`)
selected by a `dialect` option, without touching the public API.

---

## 17. Security Considerations

* **Arg injection into the CLI.** Model references passed to the CLI control plane
  (`docker model pull/rm/configure/run …`) MUST go through `QuoteArgumentIfNeeded`;
  references can carry shell metacharacters (`ai/x:v$(whoami)`), and the quoting
  logic escapes `"`/backslashes and wraps in quotes. Tests cover `"; rm -rf /`,
  backticks, `$()` in references. **Prompts are not a CLI concern** — inference
  rides a JSON HTTP body (no shell), so prompt-based shell injection is
  structurally impossible.
* **Endpoint trust.** `localhost:12434` is unauthenticated by default. We do not
  send credentials there. For `GenericOpenAiModelRunner` against remote
  endpoints, support a bearer token via `WithApiKey` (header only, never logged).
* **No secret logging.** API keys and full prompts are redacted from
  `ErrorContext` / logs (truncate prompts, mask `Authorization`).
* **SSRF surface.** `ModelRunnerEndpoint.Custom(uri)` lets callers point at
  arbitrary hosts. That's intentional (generic OpenAI), but document that it is
  caller-controlled and not validated.
* **Resource exhaustion.** Loading large models can OOM the host; the
  `IgnoreRuntimeMemoryCheck` opt-out is explicit and defaults off.
* **TLS for unix sockets / remote.** Reuse the `SocketsHttpHandler` TLS config
  approach from `DockerApiConnection` for `https` custom endpoints.

---

## 18. Performance Considerations

* **Connection reuse.** One `HttpClient`/`IModelApiConnection` per
  `ModelRunnerService`, reused across calls (avoids socket exhaustion). Disposed
  on `IAsyncDisposable`.
* **Streaming without buffering.** SSE is read line-by-line via `StreamReader`;
  we never buffer the full response. Embeddings/chat non-stream paths use
  `DeserializeAsync` over the response stream (no intermediate string).
* **`ModelReference` parsing** is allocation-light and cached on the value
  object; `ToString()` memoized.
* **Inspect caching (optional).** Mirror the existing `ContainerService` 500 ms
  TTL inspect cache for `IModelStore.InspectAsync`, invalidated on
  pull/remove/tag/configure. Behind a flag; off by default to avoid staleness.
* **Benchmarks** (BenchmarkDotNet, per project policy): SSE chunk parsing
  throughput, `ModelReference.Parse` hot path, JSON (de)serialization of large
  chat responses, and an end-to-end "simulation" benchmark replaying recorded
  SSE streams through `ChatCompletionStreamAsync` with a mock connection.

---

## 19. Testing Strategy

All tests live in the single `FluentDocker.Tests` project, differentiated by
`[Trait("Category", …)]` (`Unit`, `Integration`, `PodmanIntegration`).
**Important:** the test project targets **net10.0 only** — a
`--framework net8.0` run silently no-ops.

### 19.1 Unit tests (no Docker)

* **Driver-port unit tests** using `MockDriverPack` extended with
  `Mock<IModelManagementDriver>`, `Mock<IModelRuntimeDriver>`,
  `Mock<IModelInferenceDriver>` (Moq). Verify the `ModelRunnerService` maps
  `CommandResponse` → results/exceptions and resolves ports via `SysCtl`.
* **CLI parsing tests**: feed recorded `docker model ls --json` / `inspect` /
  `ps` outputs (fixtures under `Tests/Fixtures/Dmr/`) into `ModelJsonParser`,
  assert POCO mapping. Include table-format fallback fixtures.
* **`ModelReference` tests**: the full parse/round-trip matrix (§7.1) incl.
  `hf.co`, digests, missing tags, invalid inputs.
* **Inference HTTP tests** using `MockModelApiConnection` (hand-rolled, **no
  Moq** — mirrors `MockDockerApiConnection`): returns canned OpenAI JSON for
  chat/completion/embeddings and canned **SSE byte streams** for streaming.
  Assert chunk decoding, `[DONE]` termination, mid-stream error throwing,
  cancellation.
* **Argument-quoting/security tests**: prompts with shell metacharacters →
  correct `docker model run` argv.
* **Capabilities tests**: a pack that registers only inference yields
  `SupportsManagement=false`, etc.

### 19.2 Integration tests (`[Trait("Category","Integration")]`)

Guarded by an availability probe (`docker model status` succeeds); **skip
gracefully** when DMR isn't installed, exactly like the existing Docker-
availability gating. Use a tiny model (`ai/smollm2` or `ai/qwen2.5:0.5b`) to
keep CI fast.

* Pull → list → inspect → run/load → chat (non-stream) → chat (stream) →
  embeddings → unload → rm, asserting state at each step.
* `configure --context-size` then verify via inspect.
* Endpoint resolution: host TCP vs. socket.
* Failure paths: inference against an un-pulled model → `ModelNotLoaded`.

### 19.3 Podman integration (`[Trait("Category","PodmanIntegration")]`)

Only meaningful once the RamaLama pack exists; gated on `ramalama` presence.
Marked **future** in TASKS.md.

### 19.4 Benchmarks

Reuse the integration fixtures with a mock connection to produce deterministic,
Docker-free benchmarks (SSE parsing, reference parsing, serialization), plus an
optional live "complex simulation" benchmark behind an env flag.

### 19.5 Mock infrastructure to add

* `MockDriverPack` partial extension: model driver mocks + `SetupModel*`
  helpers (mirroring `SetupVolumeRemove`).
* `MockModelApiConnection`: programmable responses + SSE script support.
* `DmrFixtures` static class loading JSON/SSE fixtures from embedded resources.

---

## 20. Configuration & Runtime Flags

DMR configuration spans three layers, and the design must surface all three:

1. **Per-request inference parameters** — `temperature`, `top_p`, `max_tokens`,
   `stop`, etc. These ride on each `ChatCompletionRequest`/`CompletionRequest`
   (§7.4). Transient; no persistence.
2. **Persistent per-model runtime configuration** — `docker model configure`:
   context size, backend, and engine runtime flags. Persisted by DMR against the
   model; survives reloads. Modeled by `ModelConfigureOptions` (§7.3) +
   `LlamaCppRuntimeFlags` (§7.5).
3. **Runner/host configuration** — TCP host access, port, CORS origins,
   GPU-backed inference toggle. Host-level; surfaced via enablement (§23) and
   `ModelRunnerEndpoint` (§10), not set by this library at runtime (it's Docker
   Desktop / Engine setup), but **detected** via `StatusAsync`/`VersionAsync`.

### 20.1 `configure` semantics we must honor

| Capability | DMR form | Design mapping |
|---|---|---|
| Set context window | `--context-size 8192` | `ModelConfigureOptions.ContextSize` |
| Reset context to default | `--context-size -1` | `ModelConfigureOptions.ResetContextSize` |
| Select backend | `--backend llama.cpp\|vllm\|diffusers` | `ModelConfigureOptions.Backend` (`ModelBackend`) |
| Pass engine flags | `… <model> -- --temp 0.7 --top-p 0.9` | `RuntimeFlags` (raw) / `LlamaCppRuntimeFlags` (typed) |
| vLLM HF overrides | `--hf_overrides '{"max_model_len":8192}'` | `HfOverridesJson` |
| Inspect effective config | `docker model inspect` (shows max ctx) | `IModelStore.InspectAsync` → `ModelInfo.Config` |

### 20.2 Precedence & validation

* Per-request params **override** persisted `configure` defaults for that call
  (standard OpenAI semantics); the library does not merge them — it sends what
  the caller set.
* `LlamaCppRuntimeFlags` validates ranges (e.g. `top_p ∈ [0,1]`,
  `temp ∈ [0,2]`) and throws `ArgumentOutOfRangeException` before shelling out,
  to fail fast with a clear message rather than a cryptic engine error.
* Unknown/raw flags are **not** validated (passthrough freedom is intentional).

### 20.3 Auto-injected environment variables

When DMR binds a model to a workload (notably via Compose, §22) it injects:

| Variable | Meaning |
|---|---|
| `LLM_URL` (or `<PREFIX>_URL`) | The model endpoint base URL |
| `LLM_MODEL` (or `<PREFIX>_MODEL`) | The model identifier |

A consumer running inside such a workload can construct an `IModelRunner`
straight from the environment:

```csharp
var runner = ModelRunnerEnvironment.FromEnvironment();      // reads LLM_URL/LLM_MODEL
// or with a custom prefix matching endpoint_var/model_var:
var runner = ModelRunnerEnvironment.FromEnvironment("AI_MODEL");
```

`ModelRunnerEnvironment` is a small static factory (Services) that builds a
`GenericOpenAiModelRunner` (§16.2) against the injected URL — closing the loop
with Compose.

---

## 21. Inference Backends (Engines)

DMR supports three backends. The design treats the backend as an **open value**
(`ModelBackend`, §7.3) carrying format awareness, not a closed enum, so future
engines don't require API changes.

| Backend | `--backend` | Platforms | GPU requirement | Model format | Role |
|---|---|---|---|---|---|
| **llama.cpp** (default) | `llama.cpp` | macOS, Windows, Linux | optional (CUDA/AMD/Metal/Vulkan/CPU) | GGUF (Q2_K…F16) | Text gen; local/dev |
| **vLLM** | `vllm` | Linux x86_64, Windows WSL2 (Desktop 4.54+) | NVIDIA CUDA (required) | Safetensors / HF | High-throughput prod |
| **Diffusers** | `diffusers` | Linux x86_64 / ARM64 | NVIDIA CUDA (required) | DDUF | Image generation |

Design consequences:

* **Default backend is llama.cpp** and is the only one guaranteed on the user's
  Apple-silicon hardware (Metal). `ModelRunnerCapabilities.DefaultBackend`
  reports it; `AvailableBackends` is populated from `model version` / status.
* **`ModelBackend` carries an optional `Format` hint** (`gguf`/`safetensors`/
  `dduf`) used only for diagnostics — DMR resolves format from the artifact.
* **vLLM-specific knobs** flow through `HfOverridesJson` (§20.1), not typed
  fields, since they're engine-internal and evolve quickly.
* **Diffusers / image generation** is explicitly out of the v1 inference surface
  (§26 future work) — but selecting `diffusers` as a backend and calling a
  future `IModelImageGeneration` port is left open.
* The builder exposes `WithBackend(ModelBackend.Vllm)` etc.; if a backend is
  requested that the host can't satisfy (e.g. vLLM on macOS), the failure
  surfaces from DMR at load time as `ModelRunnerException(LoadFailed)` with the
  engine's message — we don't pre-validate host GPU capability.

---

## 22. Docker Compose Integration (`models:` element)

> This is a first-class DMR surface and integrates with FluentDocker's **existing
> Compose builder** (`Builder.UseCompose`), not the model drivers. It lets an
> application declare model dependencies declaratively and have DMR wire the
> endpoint into services.

### 22.1 The `models` top-level element

```yaml
models:
  <model-key>:
    model: ai/smollm2            # required — OCI artifact id (or hf.co/…)
    context_size: 4096           # optional
    runtime_flags:               # optional — raw engine flags
      - "--no-prefill-assistant"
      - "--temp"
      - "0.7"
    # x-* platform extension attributes allowed
```

### 22.2 Service ↔ model binding

**Short syntax** (auto env vars `<KEY>_URL`, `<KEY>_MODEL`):

```yaml
services:
  app:
    models:
      - llm                      # injects LLM_URL, LLM_MODEL
```

**Long syntax** (custom env var names):

```yaml
services:
  app:
    models:
      llm:
        endpoint_var: AI_MODEL_URL
        model_var: AI_MODEL_NAME
```

### 22.3 Design: extend the Compose builder

Add a model dimension to FluentDocker's Compose support so generated/managed
`compose.yaml` can declare models and bindings:

```csharp
public interface IComposeModelBuilder        // reached via IComposeBuilder.UseModels(...)
{
  IComposeModelBuilder AddModel(string key, Action<IComposeModelSpecBuilder> spec);
  IComposeModelBuilder BindToService(string service, string modelKey,
      string endpointVar = null, string modelVar = null);
}

public interface IComposeModelSpecBuilder
{
  IComposeModelSpecBuilder WithModel(string reference);     // ai/… | hf.co/…
  IComposeModelSpecBuilder WithContextSize(int tokens);
  IComposeModelSpecBuilder WithRuntimeFlags(params string[] flags);
  IComposeModelSpecBuilder WithRuntimeFlags(Action<LlamaCppRuntimeFlagsBuilder> cfg);
}
```

* **Serialization:** the Compose builder emits the `models:` top-level element
  and the per-service `models:` short/long form. This reuses the existing
  compose-file rendering pipeline (YAML), adding a `models` section type.
* **Parsing:** when *attaching to* an existing compose project (the v3.1 feature
  that lets you attach without `up`), the parser reads any `models:` element so
  the resulting model handles are discoverable via the service API.
* **Runtime:** a Compose-launched model is managed by DMR; FluentDocker can hand
  back an `IModelRunner` bound to the injected `endpoint_var`/`model_var` for the
  service (via `ModelRunnerEnvironment`, §20.3) — so app tests can drive the
  model through the same `IModelRunner` interface.
* **Scope:** Compose model support is **additive** to the model-driver work and
  is sequenced after the core (Phase 6/7 in TASKS.md). It does **not** block the
  CLI/HTTP runner.

### 22.4 Compose ↔ runner relationship

```
compose.yaml (models: + services.models:)
        │ docker compose up   (DMR pulls/loads the model)
        ▼
   DMR runner on :12434  ──injects──▶  LLM_URL / LLM_MODEL into service env
        ▲                                         │
        └──────── IModelRunner ◀── ModelRunnerEnvironment.FromEnvironment()
```

---

## 23. Enablement & Installation

The library does not *install* DMR, but it must **detect** availability,
**surface** clear remediation, and (on Engine CE) optionally drive
install/uninstall. This shapes `IModelEngine` and the failure-mode matrix (§15).

### 23.1 Docker Desktop

* Enabled via **Settings → AI → "Enable Docker Model Runner"**.
* **Host TCP**: "Enable host-side TCP support", choose a **port** (default
  `12434`), and configure **CORS origins**.
* **GPU**: on Windows with a supported NVIDIA GPU, "Enable GPU-backed inference".
* No stable CLI to toggle these from the library — so we **detect** via
  `StatusAsync`/`PingAsync` and, when disabled, throw
  `ModelRunnerException(RunnerNotRunning | EndpointUnreachable)` with a message
  pointing at the Settings → AI toggle.

### 23.2 Docker Engine (CE, Linux)

* Installed as a CLI plugin package:
  * Debian/Ubuntu: `sudo apt-get install docker-model-plugin`
  * RPM: `sudo dnf install docker-model-plugin`
* **TCP is enabled by default on port `12434`** for Engine.
* Runner lifecycle (used for upgrades) is exposed through `IModelEngine`:
  * `InstallRunnerAsync` → `docker model install-runner [--gpu …]`
  * `UninstallRunnerAsync` → `docker model uninstall-runner [--images] [--models]`
  * Documented upgrade flow:
    `docker model uninstall-runner --images && docker model install-runner`.

### 23.3 Detection flow (used by builders & services)

```csharp
var status = await runner.StatusAsync(ct);
if (!status.Running)
    throw new ModelRunnerException(
        "Docker Model Runner is not running. On Docker Desktop enable it under " +
        "Settings → AI; on Docker Engine run `docker model install-runner`.",
        ErrorCodes.Model.RunnerNotRunning);
```

`UseModelRunner().PullIfMissing()` runs this probe lazily on first use; the probe
result feeds `ModelRunnerCapabilities`.

### 23.4 Requirements summary

| Surface | Requirement |
|---|---|
| Desktop | Recent Docker Desktop with the AI/Model Runner feature; toggle on |
| Engine | `docker-model-plugin` installed; runner installed |
| Apple Silicon | llama.cpp + Metal (host execution) — no extra setup |
| NVIDIA (vLLM/Diffusers) | CUDA drivers (Linux ≥ 575.57.08, Windows ≥ 576.57) |
| Host inference from app | TCP enabled (default on Engine; opt-in on Desktop) |

---

## 24. File & Namespace Layout

```
FluentDocker/
├── Drivers/
│   ├── IModelManagementDriver.cs
│   ├── IModelRuntimeDriver.cs
│   ├── IModelInferenceDriver.cs
│   ├── IModelDriver.cs                      (optional aggregate)
│   ├── Docker/
│   │   ├── Cli/Components/
│   │   │   ├── DockerCliModelManagementDriver.cs
│   │   │   ├── DockerCliModelRuntimeDriver.cs
│   │   │   └── Parsing/ModelJsonParser.cs
│   │   │   (no CLI inference adapter — inference is HTTP-only, §8.4)
│   │   └── Api/Components/
│   │       ├── DockerApiModelInferenceDriver.cs   (used by the CLI pack too)
│   │       └── DockerApiModelManagementDriver.cs
│   └── Models/
│       └── Connection/
│           ├── IModelApiConnection.cs
│           └── ModelApiConnection.cs
├── Model/
│   ├── Models/
│   │   ├── ModelReference.cs
│   │   ├── ModelInfo.cs
│   │   ├── RunningModel.cs
│   │   ├── ModelRunnerStatus.cs
│   │   ├── ModelRunnerVersion.cs
│   │   ├── ModelDiskUsage.cs
│   │   ├── ModelPruneResult.cs
│   │   ├── ModelPullProgress.cs
│   │   ├── ModelRunnerEndpoint.cs
│   │   ├── ModelRunnerCapabilities.cs
│   │   ├── Options/ (ModelRunOptions, ModelConfigureOptions, …)
│   │   └── Inference/
│   │       ├── ChatCompletion.cs
│   │       ├── ChatStreaming.cs
│   │       ├── Completion.cs
│   │       ├── Embeddings.cs
│   │       └── InferenceCommon.cs (Usage, OpenAiModel, ChatMessage)
│   └── Drivers/ErrorCodes.cs                (+ Model, ModelInference groups)
├── Services/
│   ├── IModelStore.cs
│   ├── IModelEngine.cs
│   ├── IModelInference.cs
│   ├── IModelRunner.cs
│   ├── IModelService.cs
│   └── Impl/
│       ├── ModelRunnerService.cs
│       ├── ModelRunnerService.Store.cs
│       ├── ModelRunnerService.Engine.cs
│       ├── ModelRunnerService.Inference.cs
│       └── ModelService.cs
├── Builders/
│   ├── IModelRunnerBuilder.cs
│   ├── IModelServiceBuilder.cs
│   ├── ModelRunnerBuilder.cs
│   ├── ModelServiceBuilder.cs
│   └── ModelDriverScopedBuilderExtensions.cs
└── Common/
    └── ModelRunnerException.cs

FluentDocker.Tests/
├── CoreTests/Model/
│   ├── ModelReferenceTests.cs
│   └── ModelJsonParserTests.cs
├── CoreTests/Service/
│   ├── ModelRunnerServiceTests.Store.cs
│   ├── ModelRunnerServiceTests.Engine.cs
│   └── ModelRunnerServiceTests.Inference.cs
├── CoreTests/Drivers/
│   ├── DockerCliModelDriverTests.cs
│   └── DockerApiModelInferenceDriverTests.cs
├── IntegrationTests/
│   └── ModelRunnerIntegrationTests.cs
├── Mocks/
│   ├── MockDriverPack.Model.cs
│   └── MockModelApiConnection.cs
├── Fixtures/Dmr/ (ls.json, inspect.json, ps.json, chat.sse, …)
└── Benchmarks/
    └── ModelRunnerBenchmarks.cs
```

Each file is kept under 500 lines; the `ModelRunnerService` and inference DTOs
are pre-split accordingly.

---

## 25. Backwards Compatibility & Versioning

* **Purely additive.** No existing interface changes except two additive members
  on `DriverCapabilities` (`SupportsModels`, `SupportsModelInference`) — these
  have `init` defaults of `false`, so existing packs compile unchanged.
* **New `driverId` semantics** are opt-in via `UseModelRunner`/`UseModel`.
* **DMR plugin version drift.** The CLI driver detects the plugin version
  (`model version`) and selects `--json` vs. table parsing; new flags are added
  behind capability checks. Document a minimum supported DMR version.
* **API path variants.** Both `/engines/llama.cpp/v1` and `/engines/v1` are
  supported via `ModelRunnerEndpoint.IncludeEngineInPath`.
* **Obsolete policy.** Nothing is deprecated. Inference DTOs are marked
  `// preview` in xmldoc so we can adjust shapes pre-1.0 of this subsystem.

---

## 26. Open Questions & Future Work

1. **Tool/function calling** in chat (`tools`, `tool_choice`, `tool_calls`) —
   DTO fields reserved; defer until a concrete need.
2. **Structured outputs** (`response_format` / JSON schema) — reserve field.
3. **Anthropic & Ollama dialects** as alternate inference adapters (§16.3).
4. **Image generation** (`/engines/diffusers/v1/images/generations`) — separate
   `IModelImageGeneration` port, NVIDIA-only.
5. **vLLM backend** specifics (Safetensors, batching) — backend string already
   abstracts this; surface vLLM-only knobs later.
6. **RamaLama / Podman** pack (§16.1) — concrete future task block.
7. **Streaming embeddings / batched embeddings** — `Input` is already a list.
8. **Auto-pull UX** — should `PullIfMissing` show progress to a console by
   default? Provide an `IProgress` hook; no console coupling in the library.
9. **Remote runners** — `ModelRunnerEndpoint.Custom` covers it; consider an
   explicit `WithRemote(uri, apiKey)` ergonomic helper.
10. **Health/observability** — expose `StatusAsync` polling + a
    `ServiceRunningState` hook so model load can be awaited like a container.

---

## Appendix A: Full Interface Listings

See §5 (public) and §6 (ports). Consolidated dependency direction:

```
Builders ──▶ Services (IModelRunner family) ──▶ Drivers (IModel*Driver ports)
                                                     ▲
                            Adapters (CLI / API) ────┘  registered in DriverPacks
Model (POCOs/DTOs) ◀── referenced by all layers (no outward deps)
```

No layer depends outward; POCOs are dependency-free; the kernel is the only
place adapters are bound to ports (composition root).

---

## Appendix B: `docker model` Command Reference

| Command | Purpose | Key flags |
|---|---|---|
| `docker model pull <ref>` | Download a model (Hub `ai/…` or `hf.co/…`) | — |
| `docker model run <ref> [PROMPT]` | One-shot prompt or interactive chat | `--debug`, `-d/--detach`, `--ignore-runtime-memory-check`, `--color auto\|yes\|no` |
| `docker model ls` / `list` | List local models | `--json`, `--openai`, `-q` |
| `docker model ps` | List running models | `--json` |
| `docker model inspect <ref>` | Detailed model info | `--format`, `--json` |
| `docker model rm <ref>` | Remove a local model | `-f/--force` |
| `docker model prune` | Remove unused models | `-a` |
| `docker model configure <ref> [-- <flags>]` | Set persistent runtime config | `--context-size N` (`-1` resets), `--backend llama.cpp\|vllm\|diffusers`, `--hf_overrides '<json>'`; engine flags after `--` |
| `docker model tag <src> <dst>` | Tag a model | — |
| `docker model push <ref>` | Push to Hub / HF | — |
| `docker model package <repo:tag>` | Build OCI artifact from GGUF | `--gguf <path>`, `--push`, `--label` |
| `docker model unload <ref>` | Unload running model | `--all` |
| `docker model df` / `system df` | Disk usage | `--json` |
| `docker model system info` | Runner info | — |
| `docker model status` | Is the runner running? | — |
| `docker model version` | CLI/engine version | — |
| `docker model logs` | Stream runner logs | `-f/--follow` |
| `docker model install-runner` | Install runner (Engine CE) | `--gpu auto\|cuda\|none` |
| `docker model uninstall-runner` | Uninstall runner (Engine CE) | `--images`, `--models` |

Model reference forms: `ai/qwen3`, `ai/qwen3:8b-q4`, `hf.co/org/repo[:quant]`,
`registry/ns/name:tag`.

---

## Appendix C: DMR REST API Reference

**Base URLs**

| Context | Base |
|---|---|
| Host (TCP) | `http://localhost:12434` |
| From container (Desktop) | `http://model-runner.docker.internal:12434` |
| From container (Engine) | `http://172.17.0.1:12434` |
| Unix socket | `$HOME/.docker/run/docker.sock` |
| OpenAI SDK base | `http://localhost:12434/engines/v1` |

**Native model-management endpoints**

| Method | Path | Purpose |
|---|---|---|
| POST | `/models/create` | Pull/create a model (`{"from":"ai/qwen3"}`), streamed progress |
| GET | `/models` | List local models |
| GET | `/models/{namespace}/{name}` | Model details |
| DELETE | `/models/{namespace}/{name}` | Remove model |

**OpenAI-compatible endpoints** (engine prefix `/engines/{engine}/v1` or `/engines/v1`)

| Method | Path | Purpose |
|---|---|---|
| GET | `/engines/{engine}/v1/models` | List engine models |
| GET | `/engines/{engine}/v1/models/{ns}/{name}` | Retrieve model |
| POST | `/engines/{engine}/v1/chat/completions` | Chat (uni + `stream`) |
| POST | `/engines/{engine}/v1/completions` | Text completion |
| POST | `/engines/{engine}/v1/embeddings` | Embeddings |

**Other dialects (future)**

| Method | Path | Dialect |
|---|---|---|
| POST | `/anthropic/v1/messages` | Anthropic |
| POST | `/anthropic/v1/messages/count_tokens` | Anthropic |
| GET | `/api/tags` | Ollama |
| POST | `/api/chat`, `/api/generate`, `/api/embeddings`, `/api/show` | Ollama |
| POST | `/engines/diffusers/v1/images/generations` | Image gen (NVIDIA) |

**Enabling host TCP:** Docker Desktop GUI toggle, or
`docker desktop enable model-runner --tcp 12434`.

---

## Appendix D: Sample JSON Payloads

**`docker model ls --json` (illustrative)**

```json
[
  {
    "id": "sha256:abc123…",
    "tags": ["ai/qwen3:latest"],
    "format": "gguf",
    "architecture": "qwen3",
    "parameters": "7B",
    "quantization": "Q4_K_M",
    "size": 4683073536,
    "created": "2026-05-01T12:00:00Z",
    "config": { "context_size": "4096" }
  }
]
```

**Chat completion request**

```json
{
  "model": "ai/qwen3",
  "messages": [
    { "role": "system", "content": "You are helpful." },
    { "role": "user", "content": "Hello" }
  ],
  "max_tokens": 256,
  "temperature": 0.7,
  "stream": false
}
```

**Chat completion response**

```json
{
  "id": "chatcmpl-1",
  "object": "chat.completion",
  "created": 1717000000,
  "model": "ai/qwen3",
  "choices": [
    { "index": 0,
      "message": { "role": "assistant", "content": "Hi there!" },
      "finish_reason": "stop" }
  ],
  "usage": { "prompt_tokens": 18, "completion_tokens": 5, "total_tokens": 23 }
}
```

**Streaming chunk (one SSE line)**

```
data: {"id":"chatcmpl-1","choices":[{"index":0,"delta":{"content":"Hi"},"finish_reason":null}]}
```

**Embeddings request/response**

```json
{ "model": "ai/embeddinggemma", "input": ["hello world"] }
```
```json
{
  "data": [ { "index": 0, "embedding": [0.0123, -0.0456, "…"] } ],
  "model": "ai/embeddinggemma",
  "usage": { "prompt_tokens": 2, "total_tokens": 2 }
}
```

---

## Appendix E: Full Configuration Reference (flags, defaults, env vars)

### E.1 `docker model configure` — persistent per-model config

| Form | Meaning |
|---|---|
| `--context-size <N>` | Max context window (tokens). |
| `--context-size -1` | Reset context size to engine default. |
| `--backend llama.cpp\|vllm\|diffusers` | Select inference backend. |
| `<model> -- <flags…>` | Everything after `--` is passed verbatim to the engine. |
| `--hf_overrides '<json>'` | vLLM only — HF model overrides (`max_model_len`, `gpu_memory_utilization`, `tensor_parallel_size`, …). |

### E.2 llama.cpp runtime flags (passed after `--`)

| Flag | Default | Range / values | `LlamaCppRuntimeFlags` |
|---|---|---|---|
| `--temp` | 0.8 | 0.0–2.0 | `Temperature` |
| `--top-k` | 40 | 1–100 | `TopK` |
| `--top-p` | 0.9 | 0.0–1.0 | `TopP` |
| `--min-p` | 0.05 | 0.0–1.0 | `MinP` |
| `--repeat-penalty` | 1.1 | 1.0–2.0 | `RepeatPenalty` |
| `--threads` | auto | int | `Threads` |
| `--threads-batch` | auto | int | `ThreadsBatch` |
| `--batch-size` | 512 | int | `BatchSize` |
| `--mlock` | off | bool | `Mlock` |
| `--no-mmap` | off | bool | `NoMmap` |
| `--n-gpu-layers` | all (if GPU) | int | `GpuLayers` |
| `--main-gpu` | 0 | int | `MainGpu` |
| `--split-mode` | layer | `none\|layer\|row` | `SplitMode` |
| `--rope-freq-base` | model | float | `RopeFreqBase` |
| `--rope-freq-scale` | model | float | `RopeFreqScale` |
| `--rope-scaling` | model | string | `RopeScaling` |
| `--no-prefill-assistant` | off | bool | `NoPrefillAssistant` |
| `--reasoning-budget` | 0 | int | `ReasoningBudget` |

### E.3 Default context sizes

| Backend | Default context |
|---|---|
| llama.cpp | 4096 tokens |
| vLLM | model's max trained size |

### E.4 Per-request inference params (on the request DTO, not `configure`)

`model` (req), `messages`/`prompt` (req), `max_tokens`, `temperature`, `top_p`,
`stop`, `presence_penalty`, `frequency_penalty`, `seed`, `stream`.

### E.5 Environment variables

| Variable | Source | Meaning |
|---|---|---|
| `LLM_URL` / `<PREFIX>_URL` | injected by Compose binding | model endpoint base URL |
| `LLM_MODEL` / `<PREFIX>_MODEL` | injected by Compose binding | model identifier |
| `DOCKER_MODEL_RUNNER_URL` | read by FluentDocker | overrides endpoint resolution (§10.2) |

`<PREFIX>` derives from the Compose `endpoint_var`/`model_var` (long syntax) or
the upper-cased model key (short syntax).

### E.6 Host/runner configuration (set via Docker, detected by library)

| Setting | Where | Default |
|---|---|---|
| Model Runner enabled | Desktop Settings → AI / Engine plugin | off (Desktop) / on (Engine) |
| Host TCP support | Desktop toggle / Engine | off (Desktop) / **on** (Engine) |
| TCP port | Desktop toggle / Engine | `12434` |
| CORS origins | Desktop toggle | none |
| GPU-backed inference | Desktop (Windows/NVIDIA) | off |

### E.7 Compose `models:` keys

| Key | Level | Meaning |
|---|---|---|
| `model` | `models.<key>` | OCI artifact id (`ai/…`) or `hf.co/…` (required) |
| `context_size` | `models.<key>` | max context tokens |
| `runtime_flags` | `models.<key>` | list of raw engine flags |
| `x-*` | `models.<key>` | platform extension attributes |
| `models: [<key>]` | `services.<svc>` | short binding → `<KEY>_URL`, `<KEY>_MODEL` |
| `endpoint_var` | `services.<svc>.models.<key>` | custom URL env var name |
| `model_var` | `services.<svc>.models.<key>` | custom model-id env var name |

---

*End of DESIGN.md*
