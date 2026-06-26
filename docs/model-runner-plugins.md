---
layout: default
title: Model Runner — Plugins
nav_order: 12
---

# Extending to non-Docker runners (plugins)

Part of the [Model Runner guide](model-runner.md). The three model ports
(`IModelManagementDriver`, `IModelRuntimeDriver`, `IModelInferenceDriver`) are
runtime-neutral — nothing in them is Docker-specific. Any OpenAI-compatible runner
(vLLM, LM Studio, a bare `llama-server`, a hosted endpoint, or your own engine) plugs
in, from a two-line inference client up to a first-class kernel driver.

## The easy path — any OpenAI-compatible endpoint

If you only need chat / completion / embeddings against an existing endpoint, you need
no kernel and no driver pack. `ModelRunnerFactory.CreateInferenceRunner` composes the
OpenAI HTTP client + the `OpenAiModelInferenceDriver` for you and returns an
`IInferenceModelRunner`:

```csharp
using System;
using System.Collections.Generic;
using FluentDocker.Drivers.Models;            // ModelRunnerFactory
using FluentDocker.Model.Models;              // ModelRunnerEndpoint
using FluentDocker.Model.Models.Inference;    // ChatCompletionRequest, ChatMessage

await using var runner = ModelRunnerFactory.CreateInferenceRunner(
    ModelRunnerEndpoint.Raw(new Uri("http://localhost:8000/v1")),  // vLLM / LM Studio / llama-server
    modelId: "Qwen/Qwen2.5-7B-Instruct",
    apiKey: null);                                                  // optional bearer token

var res = await runner.ChatCompletionAsync(new ChatCompletionRequest
{
    Model = "Qwen/Qwen2.5-7B-Instruct",
    Messages = new List<ChatMessage> { new() { Role = "user", Content = "Hello!" } }
});
Console.WriteLine(res.Choices[0].Message.Content);
```

This runner is inference-only by design: `IInferenceModelRunner` exposes only the
inference plane (+ async disposal), so there are no store/engine members that could
throw.

### Endpoints for non-DMR servers

DMR serves inference under an engine prefix (`/engines/llama.cpp/v1/…`). A third-party
OpenAI server almost never does — it exposes a plain `…/v1`. Pick the endpoint factory
to match:

| Factory | Path for `/chat/completions` | Use for |
|---|---|---|
| `ModelRunnerEndpoint.Default()` / `HostTcp()` | `/engines/llama.cpp/v1/chat/completions` | Docker Model Runner |
| `ModelRunnerEndpoint.Raw(new Uri("http://host:8000/v1"))` | `/v1/chat/completions` (verbatim base, **no** engine prefix) | vLLM / LM Studio / hosted |

So for anything that is not DMR, build the endpoint with `Raw(...)` from the server's
full `…/v1` base URL — `Raw` appends request paths to that base directly.

## The full path — a first-class driver plugin

To make your runner a kernel driver (so callers use the same
`Builder → WithinDriver(id) → UseModelRunner()` surface, with management/runtime too),
implement the port(s) you can serve and expose them from a custom `IDriverPack`:

1. **Implement the ports.** Reuse `OpenAiModelInferenceDriver` for the OpenAI HTTP data
   plane, or implement `IModelInferenceDriver` yourself; optionally implement
   `IModelManagementDriver` / `IModelRuntimeDriver` for pull / ls / load / unload / etc.
   Implement only the model ports your runtime can honor:
   `IModelManagementDriver`, `IModelRuntimeDriver`, and/or `IModelInferenceDriver` — so no
   port is forced to stub methods.
2. **Expose them from an `IDriverPack`.** A pack implements `ISysCtl` +
   `IDriverInterfaceResolver` and resolves a requested port type to your adapter. The
   built-in `DockerCliDriverPack` is the reference implementation — it registers each port
   in a `Dictionary<Type, object>` and resolves by type. Register only the ports you serve;
   a pack that registers just `IModelInferenceDriver` is accepted.
3. **Register the pack on the kernel and consume it fluently:**

```csharp
var kernel = await FluentDockerKernel.Create(NullLoggerFactory.Instance)
    .WithDriver("vllm", d => d.UseCustomDriverPack(new VllmDriverPack()).AsDefault())
    .BuildAsync();

await using var runner = await new Builder()
    .WithinDriver("vllm", kernel)
    .UseModelRunner().ForModel("Qwen/Qwen2.5-7B-Instruct")
    .BuildAsync();

var reply = await runner.ChatAsync("Hi");
```

`UseModelRunner()` is gated by capability detection that accepts **any** of the three
ports, so an inference-only pack passes. If a caller then invokes a store/engine
operation your pack does not serve, the runner throws a clear `NotSupportedException`
naming the missing capability — feature-detect via `runner.Capabilities` first.

## Advertising your backend

By default a custom runner reports **no** backend (`Capabilities.DefaultBackend == null`)
rather than pretending to be `llama.cpp`. To advertise your engine, implement the optional
`IModelBackendInfo` on your runtime (or inference) driver:

```csharp
public sealed class VllmRuntimeDriver : IModelRuntimeDriver, IModelBackendInfo
{
    public string DefaultBackend => "vllm";
    public IReadOnlyList<string> AvailableBackends => new[] { "vllm" };
    // … IModelRuntimeDriver members …
}
```

The runner then surfaces it through `runner.Capabilities.DefaultBackend` /
`AvailableBackends`, so capability-based feature detection reports the real engine
instead of a hardcoded assumption.

## See also

- [Model Runner guide](model-runner.md) · [Compose models](model-runner-compose.md)
