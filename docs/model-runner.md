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

await using var runner = new Builder()
    .WithinDriver("docker", kernel)
    .UseModelRunner()
    .ForModel("ai/smollm2")
    .WithContextSize(8192)        // optional — persisted via `docker model configure`
    .PullIfMissing()             // optional — pulls at build if absent
    .Build();

// One-shot chat against the default model
var reply = await runner.ChatAsync("Reply with a single word.");

// Streaming, token by token
await foreach (var token in runner.ChatStreamAsync("Count: one two three"))
    Console.Write(token);

// Embeddings
var vector = await runner.EmbedAsync("hello world",
    model: FluentDocker.Model.Models.ModelReference.Parse("ai/embeddinggemma"));
```

By default `UseModelRunner()` uses **CLI** for management/runtime and **HTTP**
(`:12434`) for inference. Management failures surface as `ModelRunnerException`
carrying the originating error code and diagnostic context; streaming faults are
thrown mid-enumeration.

## Managing models

```csharp
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
var flags = new LlamaCppRuntimeFlags { Temperature = 0.7, TopP = 0.9, TopK = 40 };
await runner.ConfigureAsync(model, new ModelConfigureOptions { RuntimeFlags = flags.ToArgs() });
```

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

## Endpoints

`ModelRunnerEndpoint` describes where (and how) to reach the inference surface:

```csharp
ModelRunnerEndpoint.HostTcp();            // http://localhost:12434 (host process)
ModelRunnerEndpoint.ContainerInternal();  // http://model-runner.docker.internal:12434
ModelRunnerEndpoint.UnixSocket();         // $HOME/.docker/run/docker.sock
ModelRunnerEndpoint.Custom(new Uri("https://api.example.com"));
```

Resolution when unspecified: `ModelRunnerEndpoint.Default()` returns the
`DOCKER_MODEL_RUNNER_URL` env var when set, otherwise host TCP
(`http://localhost:12434`). The container-internal DNS and unix-socket forms are
*not* probed automatically — select them explicitly via `ContainerInternal()` /
`UnixSocket()` (or `WithEndpoint(...)`).

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
  the builder. See [architecture](architecture.html) for the kernel/driver model.

## Error handling & known limitations

Management/runtime failures throw `ModelRunnerException` carrying the originating
`ErrorCode` and a diagnostic `ErrorContext` (driver, operation, exit code, stderr).
Streaming faults are thrown **mid-enumeration**. Common cases:

| Situation | Surfaced as |
|---|---|
| Runner not running / TCP endpoint disabled | inference throws `EndpointUnreachable`; management/runtime still work over the CLI |
| Model not pulled | inference throws `ModelNotLoaded` (auto-pull with `PullIfMissing`) |
| Malformed SSE chunk | `ModelRunnerException` mid-stream |

Known limitations (acceptable for v3.2.0; revisit as needed):

- **Re-pull of an already-present model** is reported as success even if the
  underlying `docker model pull` errors — `PullAsync` confirms availability via
  `InspectAsync`, so a genuine *first* pull failure is still caught.
- **`StatusAsync` running-detection is substring-based** and does not distinguish
  "installed-but-down" from "not installed".
- Native `/models/create` NDJSON **error** events aren't surfaced as failures (only
  progress is parsed); `PullAsync` confirms via inspect.

## Compose `models:` integration

Docker Compose has a first-class `models:` element. FluentDocker emits it as a
small **overlay** file that merges with your own compose file (Compose merges
multiple `-f` files), so it slots into the existing file-path compose builder:

```csharp
using System;
using System.IO;
using FluentDocker.Builders.Compose;

var overlay = new ComposeModelBuilder();
overlay.AddModel("llm", m => m
    .WithModel("ai/smollm2")
    .WithContextSize(4096)
    .WithRuntimeFlags("--temp", "0.7"));
overlay.BindToService("app", "llm");                                   // short: LLM_URL / LLM_MODEL
overlay.BindToService("worker", "llm", "AI_MODEL_URL", "AI_MODEL_NAME"); // long: custom env vars

// Use a unique file name — a fixed temp path can collide between processes/runs.
var overlayPath = overlay.WriteOverlay(
    Path.Combine(Path.GetTempPath(), $"fluentdocker-models-{Guid.NewGuid():N}.overlay.yaml"));

new Builder().WithinDriver("docker", kernel)
  .UseCompose(c => c.WithComposeFiles("docker-compose.yml", overlayPath))
  .Build();
```

The overlay renders the top-level `models:` map and per-service `models:` bindings:

```yaml
services:
  app:
    models:
      - llm
  worker:
    models:
      llm:
        endpoint_var: AI_MODEL_URL
        model_var: AI_MODEL_NAME
models:
  llm:
    model: ai/smollm2
    context_size: 4096
    runtime_flags:
      - "--temp"
      - "0.7"
```

When attaching to an existing compose project, `ComposeModelBuilder.Parse(yaml)`
reads the `models:` map and per-service bindings back out. A service that binds a
model receives `LLM_URL` / `LLM_MODEL` (or the custom names), so code inside it can
reconstruct a runner via `ModelRunnerEnvironment.FromVariables(endpointVar, modelVar)`.

## Enablement

The library detects but does not install DMR:

- **Docker Desktop** — enable under *Settings → AI → Enable Docker Model Runner*
  (and turn on host-side TCP for inference).
- **Docker Engine (CE)** — install the `docker-model-plugin`; TCP is on by default.
  `runner.InstallRunnerAsync(...)` drives `docker model install-runner`.

`runner.StatusAsync()` reports whether the runner is running.

## Testing

The model integration tests are tagged `[Trait("Category", "Integration")]` and
**skip cleanly** when DMR is not running. Unit tests use `MockDriverPack`
(model ports) and a hand-rolled `MockModelApiConnection` (programmable JSON + SSE),
plus embedded fixtures captured from a real DMR.

## See also

- [Getting Started](getting-started.html) · [Containers](containers.html) · [Compose](compose.html) · [Architecture](architecture.html)
- Runnable sample: [`Examples/ModelRunner`](https://github.com/mariotoffia/FluentDocker/tree/master/Examples/ModelRunner)
