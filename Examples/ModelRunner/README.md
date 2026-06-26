# ModelRunner (v3.2 Preview)

Demonstrates FluentDocker's **Docker Model Runner** (local LLM) support behind the
same `Builder → WithinDriver → UseModelRunner()` pattern used for containers:

- **Declarative setup via the fluent builder** — each scenario selects its model and
  pulls it if missing in the builder chain (`ForModel` / `WithContextSize` /
  `PullIfMissing`), then runs inference
- One-shot chat (`ChatAsync`) and **streaming** chat (`ChatStreamAsync`)
- Embeddings (`EmbedAsync`) with a dedicated embedding model
- A model as a **managed service** (`UseModel`) that loads on start and unloads on dispose
- Listing local models

Inference is served over the OpenAI-compatible HTTP API on `:12434`; management and
runtime control use the `docker model` CLI. Transport is an internal adapter detail —
you only ever code against `IModelRunner`.

## Prerequisites

- .NET 10 SDK (the repo's global.json pins 10.0.100) and Docker Desktop / Docker Engine
- Requires Docker Desktop 4.40+ (or Docker Engine with the `docker-model-plugin` installed).
- **Docker Model Runner enabled** — Docker Desktop → *Settings → AI → Enable Docker
  Model Runner*, with host-side TCP turned on (Engine: install `docker-model-plugin`).

The sample uses tiny models (`ai/smollm2` ≈ 256 MiB, `ai/embeddinggemma`) so the first
run stays quick. If the runner is not enabled the program prints guidance and exits.

## Run

From the repository root (the project multi-targets `net8.0` and `net10.0`, so pick a
framework):

```bash
dotnet run --project Examples/ModelRunner -f net10.0
```

See [docs/model-runner.md](../../docs/model-runner.md) for the full guide, including
`WithModel(...)` to wire a model into a container, Compose `models:` integration, and
splitting management/inference across drivers with `WithInferenceDriver`.
