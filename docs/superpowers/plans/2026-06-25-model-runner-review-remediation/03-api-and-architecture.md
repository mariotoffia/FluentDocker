# Plan 03 — Public API & Architecture

> REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans. TDD for each task.

**Goal:** Settle the model-runner public surface **before** it solidifies in a release: remove the interface break, decouple builder/runner abstractions, and tidy the DTO copy.

**Architecture:** All changes are additive or move methods to extension/factory layers so existing consumers don't break. The `WithModels` interface break is the only must-fix; D15/D16 are recommended API-shape hardening; D18 is optional cleanup.

**Tech Stack:** C# extension methods, interface segregation, factory pattern.

## Global Constraints

See [README.md](README.md). The project multi-targets `net8.0;net10.0` (both support default interface methods, but we are NOT using them here per the maintainer's extension-method decision). New tests `[Trait("Category","Unit")]`.

---

## Task 1: A2 + D17 — Move `WithModels` to an extension method typed `Action<IComposeModelBuilder>`

**Files:**
- Modify: `FluentDocker/Builders/BuilderInterfaces.cs:212-222` (remove the interface member)
- Modify: `FluentDocker/Builders/InternalBuilders.cs:184-190` (make it internal, interface-typed)
- Modify: `FluentDocker/Builders/Compose/ComposeModelBuilder.cs` (ensure `: IComposeModelBuilder`)
- Create: `FluentDocker/Builders/Compose/ComposeBuilderModelExtensions.cs`
- Test: `FluentDocker.Tests/CoreTests/BuilderTests/BuilderModelExtensionsTests.cs` (already exists)

**Why:** Verified — `IComposeBuilder.WithModels` is a new abstract member on a public interface (source-breaks external implementers) and exposes the concrete `ComposeModelBuilder`. Decision: extension method, matching the existing `UseModelRunner`/`UseModel` convention (kept off `IBuilder`). An extension in the same assembly can reach the `internal ComposeBuilder`.

**Interfaces:**
- Produces: `public static IComposeBuilder WithModels(this IComposeBuilder builder, Action<IComposeModelBuilder> configure)`.
- Consumes: an `internal IComposeBuilder WithModelsInternal(Action<IComposeModelBuilder> configure)` on `ComposeBuilder`.

- [ ] **Step 1: Confirm/extend `ComposeModelBuilder` implements the interface**

Verify the class declares `: IComposeModelBuilder`. If `IComposeModelBuilder` is missing any member used by the overlay rendering, add it. Run: `dotnet build` to confirm.

- [ ] **Step 2: Remove `WithModels` from the public interface**

Delete the member at `BuilderInterfaces.cs:212-222` (the doc-comment + `IComposeBuilder WithModels(System.Action<Compose.ComposeModelBuilder> configure);`).

- [ ] **Step 3: Make the concrete method internal + interface-typed**

In `InternalBuilders.cs`, change the implementation:

```csharp
internal IComposeBuilder WithModelsInternal(Action<IComposeModelBuilder> configure)
{
  ArgumentNullException.ThrowIfNull(configure);
  _models ??= new ComposeModelBuilder();
  configure(_models);     // ComposeModelBuilder : IComposeModelBuilder
  return this;
}
```

- [ ] **Step 4: Add the extension method**

Create `ComposeBuilderModelExtensions.cs`:

```csharp
using System;

namespace FluentDocker.Builders.Compose
{
  /// <summary>Model-runner extensions for <see cref="IComposeBuilder"/>, kept off the
  /// interface to avoid breaking external implementers (mirrors UseModelRunner/UseModel).</summary>
  public static class ComposeBuilderModelExtensions
  {
    /// <summary>Adds a first-class Compose <c>models:</c> overlay (Docker Model Runner).</summary>
    public static IComposeBuilder WithModels(this IComposeBuilder builder, Action<IComposeModelBuilder> configure)
    {
      ArgumentNullException.ThrowIfNull(builder);
      if (builder is ComposeBuilder cb)
        return cb.WithModelsInternal(configure);
      throw new NotSupportedException(
          $"WithModels requires the built-in compose builder; got {builder.GetType().Name}.");
    }
  }
}
```

- [ ] **Step 5: Update existing call sites / tests**

`grep -rn "\.WithModels(" FluentDocker FluentDocker.Tests Examples` — every caller still compiles (extension method has the same call shape) **provided** the namespace `FluentDocker.Builders.Compose` is in scope. Add the `using` where needed. The lambda param type changes from `ComposeModelBuilder` to `IComposeModelBuilder`; if any caller used a concrete-only member, expose it on the interface.

- [ ] **Step 6: Run tests + lint**

Run: `make test` and `make lint`. Expected PASS / exit 0.

- [ ] **Step 7: Commit**

```bash
git commit -am "refactor(api): move IComposeBuilder.WithModels to an extension typed IComposeModelBuilder (A2,D17)"
```

---

## Task 2: D15 + D16 — Narrow the inference-only runner + move composition to a factory

**Files:**
- Create: `FluentDocker/Services/IInferenceModelRunner.cs`
- Modify: `FluentDocker/Services/Impl/GenericOpenAiModelRunner.cs` (also implement the narrow interface)
- Create: `FluentDocker/Drivers/Models/ModelRunnerFactory.cs` (Docker composition)
- Modify: `FluentDocker/Services/ModelRunnerEnvironment.cs:107-118` (depend on the factory, not concrete Docker types)
- Test: `FluentDocker.Tests/CoreTests/.../ModelRunnerEnvironmentTests.cs`

**Why:** Verified — `IModelRunner` fuses store+engine+inference+disposal, and `GenericOpenAiModelRunner` throws `NotSupportedException` for 15+ store/engine members (ISP smell). And `ModelRunnerEnvironment.CreateRunner` `new`s `ModelApiConnection` + `DockerApiModelInferenceDriver` (layering leak). Both are additive fixes: callers of an OpenAI-compatible endpoint get a narrow `IInferenceModelRunner` that only exposes what works; composition of Docker types moves out of the Services layer.

**Interfaces:**
- Produces: `public interface IInferenceModelRunner : IModelInference, IAsyncDisposable {}`; `GenericOpenAiModelRunner : IModelRunner, IInferenceModelRunner`; `ModelRunnerFactory.CreateInferenceRunner(endpoint, modelId, apiKey) -> IInferenceModelRunner`.

- [ ] **Step 1: Add the narrow interface**

```csharp
namespace FluentDocker.Services
{
  /// <summary>An inference-only model runner (OpenAI-compatible endpoint). Exposes only the
  /// inference plane + async disposal — no store/engine members that would throw NotSupported.</summary>
  public interface IInferenceModelRunner : IModelInference, System.IAsyncDisposable { }
}
```

- [ ] **Step 2: Have `GenericOpenAiModelRunner` also implement it**

It already implements every `IModelInference` member, so just add `IInferenceModelRunner` to its base list. No new code.

- [ ] **Step 3: Extract a Docker-layer factory**

Move the body of `ModelRunnerEnvironment.CreateRunner` into `ModelRunnerFactory` (Drivers/Models layer, where Docker types belong):

```csharp
namespace FluentDocker.Drivers.Models
{
  public static class ModelRunnerFactory
  {
    public static Services.IInferenceModelRunner CreateInferenceRunner(
        ModelRunnerEndpoint endpoint, string modelId, string apiKey)
    {
      var connection = new Connection.ModelApiConnection(endpoint, apiKey: apiKey);
      var inference = new Docker.Api.Components.DockerApiModelInferenceDriver(connection, endpoint);
      InferenceModelId? inferenceId = string.IsNullOrWhiteSpace(modelId) ? null : new InferenceModelId(modelId);
      var model = ModelReference.TryParse(modelId, out var r) ? r : null;
      return new Services.Impl.GenericOpenAiModelRunner(endpoint, model, inference, connection.PingAsync, connection, inferenceId);
    }
  }
}
```

- [ ] **Step 4: `ModelRunnerEnvironment` delegates to the factory and returns the narrow type**

Replace the concrete `new`s in `ModelRunnerEnvironment.cs` with `ModelRunnerFactory.CreateInferenceRunner(...)`. If callers currently consume `IModelRunner`, keep a wrapper method returning `IModelRunner` too (still satisfied by `GenericOpenAiModelRunner`) so nothing breaks; prefer exposing `IInferenceModelRunner` from inference-only entry points.

- [ ] **Step 5: Test** — `ModelRunnerEnvironment`/factory returns a runner whose static type is `IInferenceModelRunner`; assert it does NOT expose store/engine and that inference works against a mock connection.

- [ ] **Step 6: Run tests + lint, commit**

```bash
git commit -am "refactor(model): add IInferenceModelRunner + ModelRunnerFactory; remove Docker coupling from Services (D15,D16)"
```

---

## Task 3 (optional cleanup): D18 — Explicit DTO copy constructors

**Files:**
- Modify: `FluentDocker/Model/Models/Inference/ChatCompletion.cs:26-29`
- Modify: `FluentDocker/Model/Models/Inference/Completion.cs:26-29`
- Consider deleting: `FluentDocker/Model/Models/Inference/InferenceDtoCopy.cs`
- Test: existing inference DTO tests

**Why:** Verified — copy constructors round-trip through JSON + reflection (`InferenceDtoCopy.CopyInto`). For small, stable DTOs, explicit per-field copies are clearer and avoid serialization overhead. This is a **judgment-call cleanup**, not a defect — only do it if the team prefers explicitness over the "auto-includes future properties" robustness the round-trip buys.

- [ ] **Step 1: Write explicit copy constructors** copying every property and cloning mutable collections, e.g.:

```csharp
public ChatCompletionRequest(ChatCompletionRequest other)
{
  ArgumentNullException.ThrowIfNull(other);
  Model = other.Model;
  Messages = other.Messages is null ? null : new List<ChatMessage>(other.Messages);
  Temperature = other.Temperature;
  // ... copy EVERY property; deep-copy each List<>/Dictionary<> ...
}
```

- [ ] **Step 2: Guard against drift** — add a test that constructs a fully-populated request, copies it, mutates the copy's lists, and asserts the original is unchanged AND every property transferred. This catches a forgotten field if a property is added later.

- [ ] **Step 3: Remove `InferenceDtoCopy`** if no other caller remains (`grep -rn InferenceDtoCopy`). Run tests + lint.

- [ ] **Step 4: Commit**

```bash
git commit -am "refactor(dto): explicit copy constructors for inference requests (D18)"
```

---

## Self-review checklist

- [ ] `IComposeBuilder` no longer declares `WithModels`; the extension compiles for all existing callers.
- [ ] `ComposeModelBuilder : IComposeModelBuilder` and the lambda param is the interface.
- [ ] `GenericOpenAiModelRunner` implements both `IModelRunner` (back-compat) and `IInferenceModelRunner` (narrow).
- [ ] No `new ModelApiConnection`/`new DockerApiModelInferenceDriver` remains in `Services/`.
- [ ] D18: copy test mutates copy and asserts original untouched (deep copy verified).
- [ ] `make test` + `make lint` green.
- [ ] `wc -l` on each touched file ≤ 500.
