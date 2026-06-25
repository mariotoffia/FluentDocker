# Model Runner (v3.2.0) Review Remediation — Plan Index

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement each sub-plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Resolve the verified findings from the model-support code review and re-shape CI test gating per maintainer requirements, so the branch is release-ready.

**Source review date:** 2026-06-25 · **Branch:** `featrure/model-support` · **Target merge:** `master`

This is an **index**. The work is split into four independently-shippable sub-plans (one per subsystem) so each file stays focused and under the project's 600-line doc limit:

| # | Sub-plan | Covers | Why grouped |
|---|----------|--------|-------------|
| 01 | [CI & test gating](01-ci-and-test-gating.md) | Maintainer CI requirements (1–3), A1 lint fix, finding #4 DMR gate | All touch `.github/workflows/ci.yml` + `Makefile` |
| 02 | [Correctness fixes](02-correctness-fixes.md) | A3 TLS, B7 quoting, B8 process-kill, B10 inference-driver, C11 inference contract, C12 streaming timeout, C14 parser, D19 NaN, D20 error code | Behavioral bug fixes with unit tests |
| 03 | [API & architecture](03-api-and-architecture.md) | A2/D17 `WithModels`, D15 `IModelRunner` split, D16 layering, D18 DTO copy | Public-API shape — settle before release |
| 04 | [Docs & metadata](04-docs-and-metadata.md) | A6 README embed, C13 endpoint docs, D21 changelog, D22 package-readme link, A5 examples nit | Documentation/metadata only |

---

## Verification verdicts (what we actually confirmed)

Each finding was verified against the real code by a dedicated read-only agent. **3 findings were inaccurate or overstated** — do not implement those as written.

| # | Finding | Verdict | Action |
|---|---------|---------|--------|
| A1 | `make lint` whitespace fail at `BuilderModelExtensionsTests.cs:312` | ✅ Confirmed (exit 2) | Plan 01 |
| A2 | `IComposeBuilder.WithModels` public-API break | ✅ Confirmed | Plan 03 |
| A3 | TLS hostname-mismatch rejection wired into **existing** `DockerApiConnection` | ✅ Confirmed (broader than stated) | Plan 02 |
| A5 | Examples `dotnet run` needs `-f` | ❌ **Refuted** — bare run picks first TFM (net8.0), doesn't fail | Plan 04 (clarity nit only) |
| A6 | README pulls `ai/smollm2`, embeds `ai/embeddinggemma` | ✅ Confirmed | Plan 04 |
| B7 | Windows path quoting doubles all backslashes | ✅ Confirmed | Plan 02 |
| B8 | Oversized stdout doesn't `Kill()` child | ⚠️ Partial — `Dispose()` closes pipes; no explicit kill | Plan 02 |
| B9 | Compose overlay render-before-validate / temp leak | ❌ **Refuted** — validation is inside `EmitOverlay` before write; cleanup guaranteed | None (no action) |
| B10 | Stale inference-driver selection (last call doesn't win) | ✅ Confirmed (both builders) | Plan 02 |
| C11 | Non-streaming inference throws despite `CommandResponse<T>` | ⚠️ Partial — only null/parse failures throw; HTTP errors already return `Fail` | Plan 02 (narrowed) |
| C12 | Streaming has no idle/read timeout | ✅ Confirmed (documented trade-off) | Plan 02 |
| C13 | Default endpoint assumes `localhost:12434` | ✅ Confirmed (nuance — env override exists) | Plan 04 (docs/UX) |
| C14 | CLI table parsing uses fixed field positions | ✅ Confirmed (caveat: `ps`/`df` have no `--json`) | Plan 02 |
| D15 | `IModelRunner` fat interface + `NotSupportedException` | ✅ Confirmed | Plan 03 |
| D16 | `ModelRunnerEnvironment` constructs Docker types | ✅ Confirmed | Plan 03 |
| D17 | `WithModels` exposes concrete `ComposeModelBuilder` | ✅ Confirmed | Plan 03 (with A2) |
| D18 | DTO copy via JSON round-trip + reflection | ✅ Confirmed (trade-off) | Plan 03 |
| D19 | `LlamaCppRuntimeFlags` accepts NaN/Infinity | ✅ Confirmed (minor) | Plan 02 |
| D20 | `UninstallRunnerAsync` reports `InstallFailed` | ✅ Confirmed | Plan 02 |
| D21 | Changelog overstates native `/models*` management API | ✅ Confirmed | Plan 04 |
| D22 | Package README links `master/docs/model-runner.md` (not yet on master) | ✅ Confirmed | Plan 04 |
| #4 | DMR release gate uses hosted runners → self-skips green | ✅ Confirmed | Plan 01 |
| Q3 | Separate "model" test category? | ✅ **No** — model tests are `Category=Integration` already | Plan 01 |

---

## Maintainer decisions (locked 2026-06-25)

These were chosen by the maintainer and override the reviewer's default mitigations:

1. **A3 TLS** → **Opt-in/configurable.** Keep default-strict (reject hostname/SAN mismatch) but add a flag (`AllowTlsHostnameMismatch`, default `false`) so users who connect by IP can relax it. Applies to both `DockerApiConnectionConfig` and `ModelApiConnectionConfig`.
2. **A2/D17 API** → **Extension method.** Move `WithModels` off `IComposeBuilder` into a public extension method typed `Action<IComposeModelBuilder>`. Matches the existing `UseModelRunner`/`UseModel` convention (kept off `IBuilder`).
3. **#4 DMR gate** → **Self-hosted lane.** Add a dedicated job on `runs-on: [self-hosted, dmr]`, gated by a `test-dmr` label (and a `run_dmr` dispatch input), that hard-fails if zero real DMR tests execute.

## CI gating requirements (from maintainer)

1. **Integration tests** must NOT run automatically — only when a PR carries the **`test-integration`** label (plus schedule/dispatch).
2. **Unit tests** must NOT run automatically on every PR — only when a PR carries the **`test-unit`** label, **and** they must run on **merge to main** (push to `master`/`main`/`support/**`). Integration tests must NOT run on merge to main.
3. **Model tests** get **no** dedicated category — they are `Category=Integration` and run under `test-integration`. (The self-hosted DMR lane targets them via a secondary `Requires=Dmr` trait, not a new top-level category.)

---

## Suggested execution order

Implement plans in this order; each is independently testable and mergeable.

1. **Plan 01 (CI & lint)** first — unblocks a green pipeline (A1 lint is a hard blocker) and establishes the label-gated lanes the other plans rely on for verification.
2. **Plan 02 (correctness)** — the behavioral bugs. A3 TLS first (backward-compat), then the rest.
3. **Plan 03 (API & architecture)** — settle the public surface **before** the API solidifies in a release.
4. **Plan 04 (docs & metadata)** — last, once behavior/API are final so docs match reality.

## Global constraints (apply to every task in every sub-plan)

- **Code files ≤ 500 lines, doc files ≤ 600 lines.** Use `partial class` to split. Run `wc -l` after edits.
- **Test framework:** xUnit v3 + Moq. New tests must carry `[Trait("Category", "Unit")]` (or `"Integration"`/`Requires=Dmr` where stated).
- **`make test`** runs unit tests pinned to **net10.0**. **`make test-net8`** for net8.0. CI runs both. Always end a task with a green `make test`.
- **`make lint`** = `dotnet format whitespace --verify-no-changes` + `dotnet format style --verify-no-changes`. Must pass.
- **No behavior change to refuted findings B9.** Leave that code alone.
- Manually-compiled binaries use a `.out` postfix (gitignored).
- Frequent commits — one per task. TDD: failing test first.
