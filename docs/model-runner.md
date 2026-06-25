---
layout: default
title: Model Runner (LLMs)
nav_order: 10
---

# Docker Model Runner (local LLMs)

FluentDocker can manage and consume local LLMs through **Docker Model Runner (DMR)**
— and, by extension, any OpenAI-compatible endpoint (a bare `llama-server`, vLLM,
LM Studio, or a hosted endpoint). It mirrors the existing
`Builder → WithinDriver → UseXxx` pattern, so a model handle lives in the *same*
kernel and lifecycle as your containers, networks and volumes.

> **Preview / unreleased.** This subsystem is slated for FluentDocker **v3.2.0**,
> which has **not been released yet** — it is available only by building from source on
> the feature branch. The inference DTOs are marked preview; their shapes may change
> before the subsystem reaches 1.0.

## Two surfaces, one façade

DMR exposes two surfaces, and FluentDocker keeps them behind one interface family:

| Surface | What it does | Backed by |
|---|---|---|
| **Management / runtime** | pull / ls / inspect / rm / tag / push / package / df / prune, status / version / ps / load / unload / **configure** / logs / install | the `docker model …` CLI |
| **Inference** | chat (uni + streaming), completion, embeddings, engine-model list | the OpenAI-compatible REST API on `:12434` |

The public `IModelRunner` composes three small capability interfaces
(`IModelStore`, `IModelEngine`, `IModelInference`) plus a few ergonomic helpers.
Implementations may support only a subset; feature-detect via `runner.Capabilities`.

## Quick start

```csharp
using FluentDocker.Builders;
using FluentDocker.Kernel;
using Microsoft.Extensions.Logging.Abstractions;

var kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
    .WithDockerCli("docker", d => d.AsDefault())
    .BuildAsync();

await using var runner = await new Builder()
    .WithinDriver("docker", kernel)
    .UseModelRunner()
    .ForModel("ai/smollm2")
    .WithContextSize(8192)        // optional — persisted via `docker model configure`
    .PullIfMissing()             // optional — pulls at build if absent
    .BuildAsync();               // async — avoids sync-over-async on the model pull

// One-shot chat against the default model
var reply = await runner.ChatAsync("Reply with a single word.");

// Streaming, token by token
await foreach (var token in runner.ChatStreamAsync("Count: one two three"))
    Console.Write(token);

// Embeddings — note the embedding model is a *different* artifact than the chat
// default and must be present first. Pull it (once) before embedding:
var embedModel = FluentDocker.Model.Models.ModelReference.Parse("ai/embeddinggemma");
await runner.PullAsync(embedModel);            // idempotent; or: docker model pull ai/embeddinggemma
var vector = await runner.EmbedAsync("hello world", model: embedModel);
```

By default `UseModelRunner()` uses **CLI** for management/runtime and **HTTP**
(`:12434`) for inference. Management failures surface as `ModelRunnerException`
carrying the originating error code and diagnostic context; streaming faults are
thrown mid-enumeration.

## Managing models

```csharp
using System;
using FluentDocker.Model.Models;          // ModelReference, ModelPullProgress
using FluentDocker.Model.Models.Options;  // ModelConfigureOptions

await runner.PullAsync(ModelReference.Parse("ai/smollm2"),
    progress: new Progress<ModelPullProgress>(p => Console.WriteLine($"{p.Status} {p.Fraction:P0}")));

var models  = await runner.ListAsync();
var info    = await runner.InspectAsync(ModelReference.Parse("ai/smollm2"));
var running = await runner.ListRunningAsync();
var usage   = await runner.DiskUsageAsync();

await runner.ConfigureAsync(ModelReference.Parse("ai/smollm2"),
    new ModelConfigureOptions { ContextSize = 4096 });

await runner.UnloadAsync(ModelReference.Parse("ai/smollm2"));
await runner.RemoveAsync(ModelReference.Parse("ai/smollm2"), force: true);
```

### Runtime flags (typed)

`docker model configure` passes engine flags verbatim after a `--` separator.
Use the raw list, or the validated `LlamaCppRuntimeFlags` builder which renders
into it and fails fast on out-of-range values:

```csharp
using FluentDocker.Model.Models.Options;  // LlamaCppRuntimeFlags, ModelConfigureOptions

var flags = new LlamaCppRuntimeFlags { Temperature = 0.7, TopP = 0.9, TopK = 40 };
await runner.ConfigureAsync(model, new ModelConfigureOptions { RuntimeFlags = flags.ToArgs() });
```

## Completions

Beyond the ergonomic `ChatAsync` / `ChatStreamAsync` helpers, the runner exposes the
OpenAI-compatible **text completion** surface directly. It is DTO-based: build a
`CompletionRequest` (set `Model` and `Prompt`) and read the generated text off
`CompletionResponse.Choices[i].Text`:

```csharp
using FluentDocker.Model.Models.Inference;  // CompletionRequest, CompletionResponse, CompletionChunk

// One-shot text completion
var response = await runner.CompletionAsync(new CompletionRequest
{
    Model = "ai/smollm2",
    Prompt = "Complete this sentence: Docker Model Runner is",
    MaxTokens = 64,
    Temperature = 0.7
});

Console.WriteLine(response.Choices[0].Text);

// Streaming — each CompletionChunk carries an incremental Choices[i].Text delta
await foreach (var chunk in runner.CompletionStreamAsync(new CompletionRequest
{
    Model = "ai/smollm2",
    Prompt = "Count to five:"
}))
{
    Console.Write(chunk.Choices[0].Text);
}
```

`CompletionAsync` returns a `CompletionResponse` (`Id`, `Object`, `Created`, `Model`,
`Choices`, `Usage`); `CompletionStreamAsync` yields `CompletionChunk`s (`Id`, `Model`,
`Choices`) mid-enumeration. There is no string-based completion overload — completion
is DTO-only so the full request (stop sequences, seed, sampling) is expressible.

## A model as a managed service

`IModelService` is an `IServiceAsync` — it participates in the same state machine
and hook pipeline as containers, so you can `using` it for automatic unload:

```csharp
await using var model = new Builder()
    .WithinDriver("docker", kernel)
    .UseModel("ai/smollm2")
    .WithContextSize(8192)
    .KeepRunning(false)          // unload on dispose
    .Build();

await model.StartAsync();        // load
var answer = await model.Runner.ChatAsync("Hi");
// disposed -> unloaded
```

## Wiring a model into a container

A model has **no network and no volume** — it is reached at a fixed *endpoint*.
`WithModel(...)` injects that endpoint into a container's environment
(`LLM_URL` / `LLM_MODEL`) and ensures reachability (a host-gateway alias on Docker
Engine; the internal DNS name resolves automatically on Desktop). It never creates
a network or a volume.

```csharp
new Builder()
  .WithinDriver("docker", kernel)
  .UseContainer(c => c
      .UseImage("my-app:latest")
      .WithModel(ModelReference.Parse("ai/smollm2")))   // injects LLM_URL=…/engines/v1, LLM_MODEL
  .Build();
```

`localhost` is rejected for container consumers (it would resolve to the container
itself); the default is the container-internal DNS `model-runner.docker.internal`.

> **Host-exposure note (security).** `WithModel(...)` deliberately gives the container
> stable reachability to the host's model runner (a host-gateway alias on Docker
> Engine; internal DNS on Desktop). That means any process inside a model-bound
> container can reach — and send arbitrary inference requests to — your local runner.
> Only wire models into workloads you trust; for untrusted containers, prefer a
> dedicated/remote endpoint or network isolation rather than the host gateway.

### Closing the loop from inside the container

Code running *inside* a model-bound workload can reconstruct a runner from the
injected variables:

```csharp
// Default prefix LLM -> reads LLM_URL / LLM_MODEL:
var runner = ModelRunnerEnvironment.FromEnvironment();
// A custom PREFIX -> reads <PREFIX>_URL / <PREFIX>_MODEL:
var runner = ModelRunnerEnvironment.FromEnvironment("AI_MODEL");   // AI_MODEL_URL / AI_MODEL_MODEL
// Arbitrary variable NAMES (e.g. the Compose long-form endpoint_var / model_var,
// where the model variable is AI_MODEL_NAME, not AI_MODEL_MODEL):
var runner = ModelRunnerEnvironment.FromVariables("AI_MODEL_URL", "AI_MODEL_NAME");
```

This builds a `GenericOpenAiModelRunner` against the injected URL — which also
targets any OpenAI-compatible endpoint directly (with an optional API key).

> **Trusted-config note (security).** The endpoint URLs read from the environment
> (`DOCKER_MODEL_RUNNER_URL`, `LLM_URL` / `<PREFIX>_URL`) are treated as **trusted
> configuration** — FluentDocker connects to whatever they point at. If any of these
> can be populated from untrusted input, validate the scheme/host (an allowlist) before
> use; otherwise an attacker-controlled URL could redirect inference traffic
> (an SSRF-like trust-boundary issue).

## Endpoints

`ModelRunnerEndpoint` describes where (and how) to reach the inference surface:

```csharp
ModelRunnerEndpoint.HostTcp();            // http://localhost:12434 (host process)
ModelRunnerEndpoint.ContainerInternal();  // http://model-runner.docker.internal:12434
ModelRunnerEndpoint.UnixSocket();         // $HOME/.docker/run/docker.sock
ModelRunnerEndpoint.Custom(new Uri("https://api.example.com"));        // authority only
ModelRunnerEndpoint.Custom(new Uri("https://api.example.com/v1"));     // path preserved (see below)
ModelRunnerEndpoint.Raw(new Uri("http://host:12434/engines/v1"));      // exact base, OpenAI suffix appended
```

### Where is the runner? (resolution order)

When no explicit endpoint is set, `ModelRunnerEndpoint.Default()` resolves in this order:

1. **`DOCKER_MODEL_RUNNER_URL` env var** (if set) — set this for remote runners or non-standard ports.
2. **Host TCP `http://localhost:12434`** — the default when Docker Model Runner is enabled locally.

The container-internal DNS (`ContainerInternal()`) and unix-socket (`UnixSocket()`) forms are
*not* probed automatically — select them explicitly (or via `WithEndpoint(...)`).
If the runner is unreachable the exception message names the default, the env var, and these
alternatives; see also `ErrorCodes.ModelInference.EndpointUnreachable` in the *Error handling* section.

A few endpoint subtleties worth knowing:

- **`Custom(uri)` preserves a non-root path.** `Custom(new Uri("https://host"))`
  (authority only, or a `"/"` path) appends the DMR engine suffix
  `/engines/{engine}/v1/…`. But `Custom(new Uri("https://host/v1"))` **preserves the
  `/v1`** and only appends the OpenAI route (i.e. it behaves like `Raw`) — so a
  hand-written OpenAI base URL is honored rather than silently rewritten.
- **`UnixSocket()` is a preview form.** It assumes the runner serves `/engines/…`
  directly on that socket; the Docker Desktop host socket may require a routing prefix
  that is not yet applied/verified. Prefer the TCP endpoint until you've confirmed the
  socket route for your platform.
- **Inference is always reached at `ModelRunnerEndpoint.Default()` — it is NOT derived
  from the active `docker` context.** Management/runtime honor the context host
  (`docker -H …`), but the inference data plane uses `DOCKER_MODEL_RUNNER_URL` (else
  `localhost:12434`). If your runner is **remote** (a remote/TLS docker context, or a
  context-switched daemon), set `DOCKER_MODEL_RUNNER_URL` or pass an explicit
  `WithEndpoint(ModelRunnerEndpoint.Custom(...))` — otherwise management goes to the
  remote host while inference still targets localhost.

## Capabilities

```csharp
if (runner.Capabilities.SupportsStreaming) { /* … */ }
```

A runner advertises what its resolved driver can do. Inference is served over the
OpenAI-compatible HTTP data plane (the `:12434` endpoint), so whenever an
inference port is present the runner supports both streaming **and** embeddings —
there is no transport to pick. *How* a driver satisfies the inference contract
(HTTP, here — the `docker model` CLI cannot stream tokens or embed) is an internal
adapter detail, never a caller-facing choice.

## Manage with one driver, infer with another

Management/runtime and inference are independent ports, so you can keep the control
plane on the scoped driver while routing inference elsewhere. Use `WithEndpoint` to
repoint inference at a different address, or `WithInferenceDriver` to hand it an
explicit `IModelInferenceDriver` or the inference port of another registered driver
(resolved at build time). The supplied/resolved inference plane is owned by the
caller, and `WithInferenceDriver` takes precedence over `WithEndpoint`:

```csharp
await using var runner = await new Builder().WithinDriver("docker", kernel) // manage here
    .UseModelRunner().ForModel("ai/qwen3")
    .WithInferenceDriver("remote-dmr")          // infer via another registered driver…
    // .WithInferenceDriver(myInferenceDriver)  // …or an explicit IModelInferenceDriver
    // .WithEndpoint(ModelRunnerEndpoint.ContainerInternal()) // …or just a different address
    .BuildAsync();
```

## Architecture (how it's wired)

The public `IModelRunner` is a thin façade over **three internal hexagonal ports**,
each resolved from the driver pack per `driverId`:

| Port | Concern | Backed by |
|---|---|---|
| `IModelManagementDriver` | distribution & local store (pull/ls/inspect/rm/tag/push/package/df/prune) | the `docker model …` CLI |
| `IModelRuntimeDriver` | runner control plane (status/version/ps/load/unload/configure/logs/install) | the `docker model …` CLI |
| `IModelInferenceDriver` | data plane (chat/completion/embeddings/engine-model list, **streaming**) | the OpenAI-compatible HTTP API on `:12434` |

Three points fall out of this split:

- **Transport is an adapter detail, never a caller choice.** The `docker model` CLI
  cannot stream tokens or embed, so the Docker CLI driver pack *composes* the HTTP
  inference adapter to satisfy `IModelInferenceDriver`. There is no transport
  selector — you ask for the port and get a full, streaming-capable implementation.
- **The pack owns the inference connection.** `DockerCliDriverPack` is
  `IAsyncDisposable`; its `HttpClient` is released when the kernel is disposed. A
  custom endpoint (`WithEndpoint`) or an injected driver (`WithInferenceDriver`)
  is instead owned by whoever supplied it.
- **One default-endpoint source of truth.** `ModelRunnerEndpoint.Default()` resolves
  `DOCKER_MODEL_RUNNER_URL` (if set) else host TCP, and is used by both the pack and
  the builder. See [architecture](architecture.md) for the kernel/driver model.

## Extending to non-Docker runners (plugins)

The three ports are runtime-neutral, so any OpenAI-compatible runner (vLLM, LM Studio,
a bare `llama-server`, a hosted endpoint, or your own engine) plugs in. Two levels:

**Inference-only, no kernel** — point `ModelRunnerFactory.CreateInferenceRunner` at any
endpoint. Use `ModelRunnerEndpoint.Raw(...)` for a non-DMR server so requests hit a plain
`…/v1` path (DMR's `/engines/llama.cpp/v1` prefix is added only by `Default()` / `HostTcp()`):

```csharp
using FluentDocker.Drivers.Models;   // ModelRunnerFactory
using FluentDocker.Model.Models;     // ModelRunnerEndpoint

await using var runner = ModelRunnerFactory.CreateInferenceRunner(
    ModelRunnerEndpoint.Raw(new Uri("http://localhost:8000/v1")),  // vLLM / LM Studio / hosted
    modelId: "Qwen/Qwen2.5-7B-Instruct");
```

**A first-class driver** — implement the port(s) you can serve (reuse
`OpenAiModelInferenceDriver`, or write your own `IModelInferenceDriver`, plus optional
management / runtime), expose them from a custom `IDriverPack` (registering only what you
serve), and register it with `WithDriver(id, d => d.UseCustomDriverPack(pack))`. Callers
then use the same `UseModelRunner()` surface; a store/engine call your pack does not serve
surfaces as a clear `NotSupportedException`. Implement the optional `IModelBackendInfo` to
advertise your engine through `Capabilities.DefaultBackend` instead of reporting none.

Full walkthrough with a worked pack: **[Writing a runner plugin](model-runner-plugins.md)**.

## Error handling & known limitations

Every failure surfaces as a single typed exception — `ModelRunnerException` (in
`FluentDocker.Common`) — carrying the originating string error **code** in its
`ErrorCode` property plus a diagnostic `Context` (driver, operation, exit code,
stderr). The codes are *constants*, not exception types: switch on `ex.ErrorCode`
against the `ErrorCodes.ModelInference.*` / `ErrorCodes.Model.*` constants (from
`FluentDocker.Model.Drivers`). Streaming faults are thrown **mid-enumeration**.

```csharp
using FluentDocker.Common;        // ModelRunnerException
using FluentDocker.Model.Drivers; // ErrorCodes

try
{
    var reply = await runner.ChatAsync("Hi");
}
catch (ModelRunnerException ex) when (ex.ErrorCode == ErrorCodes.ModelInference.EndpointUnreachable)
{
    // Runner not running / TCP endpoint disabled — management still works over the CLI.
}
catch (ModelRunnerException ex) when (ex.ErrorCode == ErrorCodes.ModelInference.ModelNotLoaded)
{
    // Model not pulled — auto-pull at build with PullIfMissing().
}
```

Common cases (the inference error code is determined by the *transport* outcome, not
guessed):

| Situation | `ex.ErrorCode` |
|---|---|
| Runner not running / TCP endpoint disabled / connection refused / DNS / socket error | `ErrorCodes.ModelInference.EndpointUnreachable` (inference fails; management/runtime still work over the CLI) |
| HTTP 404 from the inference endpoint (model not loaded/known) | `ErrorCodes.ModelInference.ModelNotLoaded` (auto-pull with `PullIfMissing`) |
| HTTP 401 (API key required/invalid) | `ErrorCodes.ModelInference.Unauthorized` |
| Any other HTTP 4xx/5xx response | `ErrorCodes.ModelInference.RequestFailed` |
| Empty / `null` response body where a payload was required | `ErrorCodes.ModelInference.StreamParseError` |
| Malformed SSE chunk, or an oversized SSE frame | `ErrorCodes.ModelInference.StreamParseError` (thrown mid-stream) |
| Mid-stream OpenAI `data: {"error":…}` frame | `ErrorCodes.ModelInference.RequestFailed` (the server's error message is preserved; thrown mid-stream) |

> Transport failure (no HTTP response) → `EndpointUnreachable`; HTTP errors map by status (401 → `Unauthorized`, 404 → `ModelNotLoaded`, otherwise `RequestFailed`). Cancellation → `OperationCanceledException`; timeout → `TimeoutException`.

Known limitations (acceptable for v3.2.0; revisit as needed):

- **Chat models can crash on load (Docker Model Runner v1.2.1) unless a context size
  is pinned.** The bundled llama.cpp aborts — `GGML_ASSERT(n_outputs >= 1)` in its
  auto *fit-params-to-device-memory* step — when a chat model is loaded with no
  explicit context. This reproduces with a raw call to
  `…/engines/llama.cpp/v1/chat/completions` (FluentDocker not involved), so it is an
  engine bug, not a library one; the engine log even suggests `-fit off`.
  **Workaround:** pin a context size — `.WithContextSize(4096)` on the runner/service
  builder (or `docker model configure <model> --context-size 4096`) — which skips the
  buggy probe and lets the model load and stream normally. Embedding models (e.g.
  `ai/embeddinggemma`) are unaffected.
- **Re-pull of an already-present model** is reported as success even if the
  underlying `docker model pull` errors — `PullAsync` confirms availability via
  `InspectAsync`, so a genuine *first* pull failure is still caught.
- **`StatusAsync` running-detection is substring-based** and does not distinguish
  "installed-but-down" from "not installed".
- **Inference always targets `ModelRunnerEndpoint.Default()` (local), not the active
  `docker` context.** With a remote/TLS context, set `DOCKER_MODEL_RUNNER_URL` or
  `WithEndpoint(...)` for the data plane (see *Endpoints* above).
- **Model management is served by the `docker model` CLI** this release. There is no native HTTP `/models*` management API in v3.2.0.

## Security

Local DMR over `http://localhost:12434` needs no transport security, but a **remote**
or shared OpenAI-compatible endpoint usually does. The inference connection
(`ModelApiConnection`) supports TLS and a bearer API key, configured through
`ModelApiConnectionConfig` and the connection's `apiKey` parameter.

### TLS for remote endpoints

`ModelApiConnectionConfig` carries the transport settings:

- **`CertificatePath`** — a directory containing `ca.pem` / `cert.pem` / `key.pem`.
  The `ca.pem` adds a custom CA to trust (a privately signed server), and
  `cert.pem` + `key.pem` supply a client certificate for mutual TLS.
- **`VerifyTls`** — defaults to `true`. Setting it to `false` disables server
  certificate verification entirely; use it only against a trusted endpoint during
  development, never in production.
- **`ConnectionTimeout`** / **`RequestTimeout`** — connect and per-request timeouts
  (the request timeout applies to non-streaming calls only; streaming relies on the
  caller's `CancellationToken`).

### API keys

A bearer token is supplied via the connection's `apiKey` parameter. When set, it is
sent as an `Authorization: Bearer …` header on every request and is **never logged**.

The `ModelApiConnection` constructor is:

```csharp
public ModelApiConnection(
    ModelRunnerEndpoint endpoint,
    ModelApiConnectionConfig config = null,
    ILoggerFactory loggerFactory = null,
    string apiKey = null)
```

### Through the fluent builder

You rarely construct the connection by hand — `WithEndpoint(endpoint, config, apiKey)`
threads the same configuration through the builder. It is available on **both**
`IModelRunnerBuilder` and `IModelServiceBuilder`:

```csharp
IModelRunnerBuilder WithEndpoint(ModelRunnerEndpoint endpoint,
    ModelApiConnectionConfig config = null, string apiKey = null);
```

```csharp
using FluentDocker.Drivers.Models.Connection;  // ModelApiConnectionConfig
using FluentDocker.Model.Models;                // ModelRunnerEndpoint

await using var runner = await new Builder().WithinDriver("docker", kernel)
    .UseModelRunner()
    .ForModel("ai/smollm2")
    .WithEndpoint(
        ModelRunnerEndpoint.Custom(new Uri("https://llm.example.com/v1")),
        new ModelApiConnectionConfig
        {
            CertificatePath = "/etc/fluentdocker/llm-certs",  // ca.pem (+ cert.pem/key.pem for mTLS)
            VerifyTls = true                                  // default; do NOT disable in production
        },
        apiKey: Environment.GetEnvironmentVariable("LLM_API_KEY"))
    .BuildAsync();
```

The builder-built connection is owned (disposed) by the runner. `IModelServiceBuilder`
exposes the identical overload for the lifecycle-first surface.

### Related trust-boundary notes

Two adjacent concerns are covered inline above and apply here too:

- **Host exposure** — `WithModel(...)` gives a container reachability to your local
  runner; only wire models into trusted workloads (see the *Host-exposure note* under
  [Wiring a model into a container](#wiring-a-model-into-a-container)).
- **Trusted endpoint configuration** — endpoint URLs read from the environment are
  treated as trusted; validate them if they can come from untrusted input to avoid an
  SSRF-like redirect (see the *Trusted-config note* under
  [Closing the loop from inside the container](#closing-the-loop-from-inside-the-container)).

## Compose `models:` integration

Docker Compose's first-class `models:` element is supported via `WithModels(...)` on the
compose builder (auto-rendered, auto-merged, auto-deleted overlay) or the lower-level
`ComposeModelBuilder` + `WriteOverlay(path)` when you want to own the file. See the
dedicated guide: **[Compose models integration](model-runner-compose.md)**.

## Prerequisites, enablement & compatibility

| Component | Requirement / notes |
|---|---|
| **Docker Desktop** | A recent build with the Model Runner feature — enable under *Settings → AI → Enable Docker Model Runner*, and turn on **host-side TCP** for the inference data plane. |
| **or Docker Engine (CE)** | Install the `docker-model-plugin`; TCP is on by default. `runner.InstallRunnerAsync(...)` drives `docker model install-runner`. |
| **`docker model` plugin (DMR)** | The subsystem is verified against **DMR v1.2.1**; it tolerates that version's CLI quirks (e.g. `inspect`/`df` have no `--json`, `purge` not `prune`). |
| **Inference endpoint** | OpenAI-compatible HTTP on `:12434` (or whatever `DOCKER_MODEL_RUNNER_URL` / `WithEndpoint(...)` points at). Required for chat/completion/embeddings; management/runtime work over the CLI without it. |
| **.NET (consuming the library)** | FluentDocker targets **net8.0** and **net10.0** — reference it from either. |
| **.NET SDK (building *this repo*)** | The **.NET 10 SDK** is required to build the solution (`global.json` pins `10.0.100`). This is distinct from the library's runtime targets above: you can *consume* FluentDocker on .NET 8, but *building the repo* needs the .NET 10 SDK. |
| **OS / platform** | Windows, macOS and Linux (DMR availability follows Docker Desktop / Engine; the runner subsystem was verified on macOS arm64). |

The library detects but does **not** install DMR. `runner.StatusAsync()` reports whether
the runner is running.

> **Running the sample.** [`Examples/ModelRunner`](https://github.com/mariotoffia/FluentDocker/tree/featrure/model-support/Examples/ModelRunner)
> multi-targets `net8.0;net10.0`, so a bare `dotnet run` fails ("specify which framework").
> Run it with an explicit framework: `dotnet run -f net10.0` (or `-f net8.0`).

## Testing

Model integration tests carry `[Trait("Category", "Integration")]` and the overlay
`[Trait("Requires", "Dmr")]`; they **skip cleanly** when DMR is absent. Unit tests use
`MockDriverPack` + a hand-rolled `MockModelApiConnection` (programmable JSON + SSE)
with embedded fixtures captured from a real DMR.

### CI test lanes

| Label / trigger | Job | Runner | What runs | Local equivalent |
|---|---|---|---|---|
| `test-unit` on PR; always on push to `master`/`main`/`support/**` | `unit-tests` | hosted (3-OS matrix) | `Category=Unit` with coverage | `make test` |
| `test-integration` on PR | `integration-tests` | hosted ubuntu | `Category=Integration` (DMR tests self-skip — no `docker model` on hosted runners) | `make test-integration` |
| `test-dmr` on PR; `workflow_dispatch run_dmr=true` | `dmr-tests` | **self-hosted `[self-hosted, dmr]`** | `Category=Integration&Requires=Dmr` with `FLUENTDOCKER_REQUIRE_DMR=1` | `make test-dmr` |

Key behaviours:

- **Unit tests** run on every merge to `master`/`main`/`support/**` and on PRs with
  the `test-unit` label. Docker not required.
- **Integration tests** run only with the `test-integration` PR label (or via
  `workflow_dispatch run_integration=true`); not on push. DMR tests self-skip on hosted
  runners (no `docker model` present).
- **DMR gate** runs on a self-hosted runner with a live `docker model` runtime.
  `FLUENTDOCKER_REQUIRE_DMR=1` hard-fails if zero DMR tests execute, so a green run
  proves real coverage rather than an empty self-skip.

The `Requires=Dmr` overlay trait makes DMR tests selectable by the dedicated gate
(`--filter "Category=Integration&Requires=Dmr"`) without removing them from the
broader `Category=Integration` lane.

## See also

- [Getting Started](getting-started.md) · [Containers](containers.md) · [Compose](compose.md) · [Architecture](architecture.md)
- Model Runner sub-pages: [Compose models](model-runner-compose.md) · [Writing a runner plugin](model-runner-plugins.md)
- Runnable sample: [`Examples/ModelRunner`](https://github.com/mariotoffia/FluentDocker/tree/featrure/model-support/Examples/ModelRunner)
