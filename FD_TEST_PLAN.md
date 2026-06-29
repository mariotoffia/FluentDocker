# FluentDocker Test Support Model-Support Fix Plan

> **For agentic workers:** Work this checklist top-to-bottom. Keep fixes small; prefer documentation and existing generic helpers before adding new fixture classes.

**Goal:** Make FluentDocker test support easy, production-ready, and documented for model-support users without adding framework-specific model fixture bloat.

**Scope reviewed:** `FluentDocker.Testing.Core` in the main assembly, the xUnit/NUnit/MSTest adapter packages, their docs/package metadata, and the current Docker Model Runner test/docs story.

---

## Verification status (second pass)

Re-scanned the code against this plan. All 15 tasks confirmed real and applicable, with two adjustments and two reconciliations:

- **Reconciled (already fixed in-flight on this branch, no action):**
    - `IModelRunner.Capabilities` / `ModelRunnerCapabilities` are now documented as *static* feature-detection (not a health check) — matches the plan's "Easy to use" note.
    - `ModelRunnerBuilder.PullIfMissing` now treats only `ErrorCodes.Model.NotFound` as "absent" (other inspect failures rethrow), with a new unit test `BuilderModelExtensionsTests.PullIfMissing.cs`. This narrows Task 6's risk but the destructive host-store mutation still stands.
- **Task 9 downgraded** to a documented sample only (no live integration test): a model-backed container reachability test needs DMR + a curl-capable image + network — too heavy for the value. Show the `WithModel` → `$LLM_URL/models` pattern in docs instead.
- **Task 4 API intent:** add ONE `ModelResource` (final name at the implementer's discretion) in `FluentDocker/Testing/Core/` that plugs into the existing generic resource fixtures/helpers (`XunitResourceFixture<TResource>`, `MsTestResourceHelpers.CreateResourceAsync`, `NUnitResourceHelpers.CreateResourceAsync`) and exposes the loaded `IModelService`/`IModelRunner`. No per-framework model fixtures. Its usage guide lives in a NEW `docs/testing/model.md`.

**Severity order for execution:** Tasks 1, 2 (production-safety) → Task 4 + 6/7/8 (model) → 10/11/12/13 (adapter) → docs (3, 5, 9-sample, 14, 15). `make check` + `make test-net8` is the gate.

---

## Tasklist

- [x] **Task 1: Make orphan cleanup safe for parallel CI**
    - Files: `FluentDocker/Testing/Core/OrphanCleanup.cs`, `FluentDocker/Testing/Core/DockerResourceOptions.cs`, `FluentDocker/Testing/Core/ResourceBase.cs`, `FluentDocker.Tests/CoreTests/Testing/*`
    - Fix: do not let `CleanupOrphansOnInit` delete live resources from sibling test sessions. Smallest acceptable fix: require an explicit suite-wide cleanup mode/session id, or preserve resources newer than a configurable age threshold.
    - Verify: unit test where two different `SessionId` values exist and the newer/live resource is not removed.

- [x] **Task 2: Stop hiding force-cleanup failures**
    - Files: `FluentDocker/Testing/Core/ResourceBase.cs`, resource implementations under `FluentDocker/Testing/Core/*Resource.cs`
    - Fix: if graceful teardown fails and force remove also fails, preserve enough failure state for CI to fail or retry instead of clearing `_provisioned` as if cleanup succeeded.
    - Verify: unit test where `TeardownAsync` and `ForceRemoveAsync` both fail and `DisposeAsync` surfaces the failure or keeps the resource retryable.

- [x] **Task 3: Define what cleanup actually covers**
    - Files: `FluentDocker/Testing/Core/OrphanCleanup.cs`, `FluentDocker/Testing/Core/ComposeResource.cs`, `FluentDocker/Testing/Core/TopologyResource.cs`, `docs/testing/core.md`
    - Fix: either document orphan cleanup as container/network/volume-only, or add labels/cleanup for compose/topology-created resources. Prefer documentation unless tests prove missing cleanup is harmful.
    - Verify: docs and behavior say the same thing.

- [x] **Task 4: Add one core model test resource**
    - Files: `FluentDocker/Testing/Core/ModelRunnerResource.cs` or equivalent, `FluentDocker.Tests/CoreTests/Testing/ModelRunnerResourceTests.cs`, `docs/testing/core.md`
    - Fix: add one `ITestResource` wrapper for model runner/model service lifecycle. Reuse `XunitResourceFixture<TResource>`, `MsTestResourceHelpers.CreateResourceAsync`, and `NUnitResourceHelpers.CreateResourceAsync`; do not add xUnit/NUnit/MSTest model fixture families.
    - Verify: public API test covers initialize/dispose, longer model timeout options, and unavailable-runner failure diagnostics.

- [x] **Task 5: Expose or document a tiny DMR probe**
    - Files: `FluentDocker.Tests/Integration/ModelRunnerIntegrationTests.cs`, `docs/testing.md`, `docs/testing/xunit.md`, `docs/testing/nunit.md`, `docs/testing/mstest.md`
    - Fix: move the current internal DMR availability pattern into docs, or expose a small helper that returns `available + reason`. Avoid cross-framework skip abstractions.
    - Verify: docs show xUnit, NUnit, and MSTest skip/require snippets.

- [x] **Task 6: Remove destructive normal DMR integration behavior**
    - Files: `FluentDocker.Tests/Integration/ModelRunnerIntegrationTests.cs`
    - Fix: gate the test that removes `ai/smollm2:latest` behind an explicit env var, or rely on unit coverage for `PullIfMissing`.
    - Verify: normal `make test-dmr` never removes a developer/CI model from the host store.

- [x] **Task 7: Make model integration tests configurable**
    - Files: `FluentDocker.Tests/Integration/ModelRunnerIntegrationTests.cs`, `docs/model-runner.md`
    - Fix: allow model refs to come from environment variables and document expected model size/download cost. Use immutable refs if Docker Model Runner supports them.
    - Verify: tests use defaults only when env vars are unset, and docs explain how to override.

- [x] **Task 8: Add one real `UseModel` DMR smoke test**
    - Files: `FluentDocker.Tests/Integration/ModelRunnerIntegrationTests.cs`
    - Fix: add a DMR-gated test for `UseModel(...).KeepRunning(false)`: start, simple inference, dispose, assert model no longer appears in running models.
    - Verify: tagged `Category=Integration` and `Requires=Dmr`; skips unless DMR is available or required.

- [x] **Task 9: Add one model-backed container smoke test or sample**
    - Files: `FluentDocker.Tests/Integration/ModelRunnerIntegrationTests.cs` or `Examples/ModelRunner/README.md`, `docs/model-runner.md`
    - Fix: show `WithModel(...)` from a container by curling `$LLM_URL/models`; no chat and no extra model download.
    - Verify: DMR-gated, tiny, and does not require a full LLM generation path.

- [x] **Task 10: Fix xUnit version clarity**
    - Files: `FluentDocker.Testing.Xunit/FluentDocker.Testing.Xunit.csproj`, `docs/testing/xunit.md`, `README.md`
    - Fix: explicitly call this package xUnit v3-only, or provide a separate v2-compatible adapter. Prefer documentation/package description first.
    - Verify: NuGet description and docs both say xUnit v3.

- [x] **Task 11: Fix nullable ergonomics in xUnit base classes**
    - Files: `FluentDocker.Testing.Xunit/XunitContainerTestBase.cs`, `XunitComposeTestBase.cs`, `XunitTopologyTestBase.cs`, fixture-base equivalents
    - Fix: expose initialized `Resource`, `Container`/`Service`, and `Kernel` as non-null throwing properties, matching concrete fixtures.
    - Verify: docs examples compile under `<Nullable>enable</Nullable>` without `!`.

- [x] **Task 12: Retire or repair the MSTest inherited base**
    - Files: `FluentDocker.Testing.MsTest/MsTestContainerFixtureBase.cs`, `docs/testing/mstest.md`, `README.md`
    - Fix: stop recommending `MsTestContainerFixtureBase`; helper methods with explicit class-owned static fields match MSTest class lifecycle. If keeping the base, key static state by derived type and document parallel limits.
    - Verify: two derived MSTest classes cannot share the wrong container/config.

- [x] **Task 13: Package adapter-specific READMEs**
    - Files: `FluentDocker.Testing.Xunit/*.csproj`, `FluentDocker.Testing.NUnit/*.csproj`, `FluentDocker.Testing.MsTest/*.csproj`, `docs/testing/*.md`
    - Fix: package each adapter's own quickstart as `PackageReadmeFile` instead of the root README.
    - Verify: `dotnet pack` contains the adapter-specific README.

- [x] **Task 14: Remove fixed names from docs examples**
    - Files: `docs/testing/core.md`, `docs/testing/xunit.md`, `docs/testing/nunit.md`, `docs/testing/mstest.md`
    - Fix: examples should not hard-code compose project, network, stack, or container names unless they also show disabling parallelism.
    - Verify: examples are parallel-safe by default.

- [x] **Task 15: Add test-support model docs**
    - Files: `README.md`, `docs/testing.md`, `docs/testing/core.md`, `docs/testing/xunit.md`, `docs/testing/nunit.md`, `docs/testing/mstest.md`, `docs/model-runner.md`
    - Fix: add a short “Testing Docker Model Runner” path: probe/skip, opt-in require env, avoid surprise `PullIfMissing`, use longer timeout, unload on cleanup, and model-backed container reachability.
    - Verify: a user can copy one xUnit/NUnit/MSTest snippet and know how it behaves in CI.

---

## Details

### Reviewed test-support files

Core test-support code used by every adapter:

- `FluentDocker/Testing/Core/ITestResource.cs`
- `FluentDocker/Testing/Core/ResourceBase.cs`
- `FluentDocker/Testing/Core/ResourceLifecycle.cs`
- `FluentDocker/Testing/Core/DockerResourceOptions.cs`
- `FluentDocker/Testing/Core/DriverSelection.cs`
- `FluentDocker/Testing/Core/OrphanCleanup.cs`
- `FluentDocker/Testing/Core/ContainerResource.cs`
- `FluentDocker/Testing/Core/ComposeResource.cs`
- `FluentDocker/Testing/Core/TopologyResource.cs`
- `FluentDocker/Testing/Core/ImageResource.cs`
- `FluentDocker/Testing/Core/NetworkResource.cs`
- `FluentDocker/Testing/Core/VolumeResource.cs`
- `FluentDocker/Testing/Core/SwarmStackResource.cs`
- `FluentDocker/Testing/Core/PodmanKubernetesResource.cs`
- `FluentDocker/Testing/Core/Plugins/*`

Framework adapter code:

- `FluentDocker.Testing.Xunit/*.cs`
- `FluentDocker.Testing.NUnit/*.cs`
- `FluentDocker.Testing.MsTest/*.cs`
- `FluentDocker.Testing.Xunit/FluentDocker.Testing.Xunit.csproj`
- `FluentDocker.Testing.NUnit/FluentDocker.Testing.NUnit.csproj`
- `FluentDocker.Testing.MsTest/FluentDocker.Testing.MsTest.csproj`
- `Directory.Packages.props`

Docs and current model-support references:

- `README.md`
- `docs/testing.md`
- `docs/testing/core.md`
- `docs/testing/xunit.md`
- `docs/testing/nunit.md`
- `docs/testing/mstest.md`
- `docs/model-runner.md`
- `Examples/ModelRunner/README.md`
- `FluentDocker.Tests/Integration/ModelRunnerIntegrationTests.cs`

### Easy to use

The current support is easy for Redis-style container tests, but not for model-support users. `docs/testing/core.md` lists container/compose/topology/swarm/podman resources only; model users must discover `UseModelRunner()`/`UseModel()` and then hand-roll lifecycle and skip logic. The lazy fix is one core `ITestResource` model wrapper plus docs using the existing generic adapter entry points.

xUnit also needs blunt version labeling. The adapter depends on `xunit.v3.extensibility.core`, but docs just say xUnit. That is a copy/paste trap for xUnit v2 projects.

Nullable ergonomics are uneven: concrete xUnit fixtures throw on uninitialized access and expose non-null properties, while base classes expose nullable properties and docs dereference them directly. Make the base classes match the concrete classes.

### Production ready

`CleanupOrphansOnInit` is unsafe as-is. Every `DockerResourceOptions` gets a unique `SessionId`; cleanup preserves only the current id. In parallel CI, one test can delete another test's live resource. Do not ship this as a convenience cleanup without an explicit suite-wide scope or age guard.

Cleanup failures are also too quiet. Several force-remove methods swallow exceptions; `ResourceBase` then clears `_provisioned`, which can make CI pass while resources leak. Test support should fail loudly on cleanup uncertainty unless explicitly configured otherwise.

DMR integration tests should not mutate the developer/CI host model store during normal runs. The current pull-if-missing integration test removes `ai/smollm2:latest` and tries to restore it. That is not production-ready behavior for a test suite.

MSTest inherited fixture state is static across the base type, not per derived test class. Two different derived classes can share the wrong resource/config and race cleanup. The helper-method pattern is simpler and matches MSTest lifecycle semantics.

### Documentation

Docs need one model testing path, not a new manual. Add a short section covering:

- probe DMR and skip unless explicitly required;
- use a longer timeout for first pull/load;
- avoid surprise `PullIfMissing()` in CI unless opted in;
- unload models on cleanup with `KeepRunning(false)`;
- use env-overridable model refs and document size/download cost;
- model-backed container smoke test via `$LLM_URL/models`, not chat generation.

Adapter NuGet READMEs currently pack the root FluentDocker README. That makes the packages look generic and hides the adapter lifecycle gotchas. Pack adapter-specific quickstarts instead.

Several docs examples use fixed names (`test-redis`, project names, network names). Fixed names are fine for a blog post, bad for a test-support library. Use generated names or explicitly disable parallelism.

### Adversarial review summary

High-risk findings to fix before calling model test support production-ready:

1. Orphan cleanup can delete live sibling test resources.
2. Force cleanup can fail silently and clear retry state.
3. No first-class model test resource or documented model test path.
4. DMR skip/require behavior is private to FluentDocker's internal xUnit tests.
5. One DMR integration test destructively removes a host model.
6. MSTest base fixture shares static state across derived classes.
7. xUnit adapter is v3-only but not labeled that way.

Skipped: framework-specific model fixture hierarchies. Add them only if the generic resource path becomes painful in real user code.

---

## Execution outcome (third pass — implemented + adversarially re-reviewed)

All 15 tasks implemented via expert subagents (production-safety, model, adapter, docs), then the
combined diff was put through a second adversarial review. Findings triaged against real code:

- **MAJOR — found and fixed (new, not in the original plan):** `NetworkResource.TeardownAsync` and
  `VolumeResource.TeardownAsync` discarded the driver's `CommandResponse`. Container/Compose teardown
  call *service* methods that throw on failure, but Network/Volume call *driver* methods that return
  `CommandResponse` and never throw — so a failed graceful remove looked like success, cleared
  `_provisioned`, and the hardened force path never ran (silent resource leak, defeating Task 2).
  Fix: both now check `.Success` / `NotFound` and otherwise throw `DriverException` while keeping the
  id/name so `DisposeAsync` engages `ForceRemoveAsync`. Red-green regression tests added to
  `NetworkResourceTests` and `VolumeResourceTests` (verified failing before the fix, passing after).
- **Accepted nits applied:** `ModelResource.Model` now readable before init (known from construction);
  the `UseModel` smoke test routes its post-dispose assertion through `SkipIfRuntimeUnstable`;
  `docs/testing/model.md` documents that model unload-on-dispose is best-effort and never throws.
- **Rejected after verification (not over-engineering / not bugs):** removing the 4-arg
  `CleanupOrphanedResourcesAsync` overload (preserves released-API source-compat; its `TimeSpan.Zero`
  is master's prior no-guard behavior, while the safe 1h default lives on the production `ResourceBase`
  path); `IsReference` Name-or-fullref match (strictly better than the pre-existing Name-only check);
  Compose/Topology `"not found"` substring matching (pre-existing best-effort pattern, out of scope).

**Gate evidence:** `make check` exit 0 (lint clean, 0 errors); `make test` → Build succeeded,
**Passed: 3856, 0 failed** (net10.0); net8.0 library build clean (0 errors). net8 *test execution*
is blocked locally by a missing `Microsoft.NETCore.App 8.0.0` runtime (only 10.0.x installed) — an
environment gap, not a code defect; CI runs the net8 suite.
