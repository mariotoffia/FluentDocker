# ModelRunner (v3.2 Feature)

Demonstrates FluentDocker's **Docker Model Runner** (local LLM) support behind the
same `Builder → WithinDriver → UseModelRunner()` pattern used for containers:

- Pull (with progress) and list local models
- One-shot chat (`ChatAsync`) and **streaming** chat (`ChatStreamAsync`)
- Embeddings (`EmbedAsync`) with a dedicated embedding model
- A model as a **managed service** (`UseModel`) that loads on start and unloads on dispose

Inference is served over the OpenAI-compatible HTTP API on `:12434`; management and
runtime control use the `docker model` CLI. Transport is an internal adapter detail —
you only ever code against `IModelRunner`.

## Prerequisites

- .NET 10.0 SDK and Docker Desktop / Docker Engine
- **Docker Model Runner enabled** — Docker Desktop → *Settings → AI → Enable Docker
  Model Runner*, with host-side TCP turned on (Engine: install `docker-model-plugin`).

The sample uses tiny models (`ai/smollm2` ≈ 256 MiB, `ai/embeddinggemma`) so the first
run stays quick. If the runner is not enabled the program prints guidance and exits.

## Run

```bash
cd ModelRunner && dotnet run
```

See [docs/model-runner.md](../../docs/model-runner.md) for the full guide, including
`WithModel(...)` to wire a model into a container, Compose `models:` integration, and
splitting management/inference across drivers with `WithInferenceDriver`.
