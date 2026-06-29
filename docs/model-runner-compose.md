---
layout: default
title: Model Runner — Compose
nav_order: 11
---

# Docker Compose `models:` integration

Part of the [Model Runner guide](model-runner.md). Docker Compose has a first-class
`models:` element. FluentDocker emits it as a small **overlay** file that merges with
your own compose file (Compose merges multiple `-f` files). The recommended way is
`WithModels(...)` on the compose builder: it renders the overlay to a managed temp
file, appends it to the compose-files list for you, and **deletes the temp file
automatically** when the compose service is torn down / disposed — no path juggling,
no manual cleanup:

```csharp
using FluentDocker.Builders;
using FluentDocker.Builders.Compose;

await using var built = await new Builder().WithinDriver("docker", kernel)
    .UseCompose(c => c
        .WithComposeFile("docker-compose.yml")
        .WithModels(m =>
        {
            m.AddModel("llm", s => s
                .WithModel("ai/smollm2")
                .WithContextSize(4096)
                .WithRuntimeFlags("--temp", "0.7"));
            m.BindToService("app", "llm");                                    // short: LLM_URL / LLM_MODEL
            m.BindToService("worker", "llm", "AI_MODEL_URL", "AI_MODEL_NAME"); // long: custom env vars
        }))
    .BuildAsync();

// The merged overlay temp file lives as long as the compose service; disposing
// `built` tears the project down AND removes the temp overlay file.
```

> **`WithModels(...)` is implemented for FluentDocker's built-in compose builder.** It
> renders and merges the overlay through the concrete `ComposeBuilder`; a third-party
> `IComposeBuilder` implementation is not accepted here. If you build compose files
> yourself, use the [manual overlay](#manual-overlay-still-supported) API below and add
> the rendered file to your own `-f` list.

## Manual overlay (still supported)

The lower-level `ComposeModelBuilder` + `WriteOverlay(path)` API remains available
when you want to own the overlay file yourself (e.g. to inspect or persist it). In
that case **you** pass it via `WithComposeFiles(...)` and **you** delete it:

```csharp
using System;
using System.IO;
using FluentDocker.Builders.Compose;

var overlay = new ComposeModelBuilder();
overlay.AddModel("llm", m => m.WithModel("ai/smollm2").WithContextSize(4096));
overlay.BindToService("app", "llm");

// Use a unique file name — a fixed temp path can collide between processes/runs.
var overlayPath = overlay.WriteOverlay(
    Path.Combine(Path.GetTempPath(), $"fluentdocker-models-{Guid.NewGuid():N}.overlay.yaml"));

try
{
    new Builder().WithinDriver("docker", kernel)
      .UseCompose(c => c.WithComposeFiles("docker-compose.yml", overlayPath))
      .Build();
}
finally
{
    // With the manual API FluentDocker never deletes the overlay — the caller owns it.
    if (File.Exists(overlayPath))
        File.Delete(overlayPath);
}
```

Both paths render the same top-level `models:` map and per-service `models:` bindings:

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

A service that binds a model receives `LLM_URL` / `LLM_MODEL` (or the custom
names), so code inside it can reconstruct a runner via
`ModelRunnerEnvironment.FromVariables(endpointVar, modelVar)`.

## See also

- [Model Runner guide](model-runner.md) · [Writing a runner plugin](model-runner-plugins.md)
