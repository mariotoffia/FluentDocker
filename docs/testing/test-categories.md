---
layout: default
title: Test Categories
parent: Testing
nav_order: 7
---

# Test Categories & Run Guide

FluentDocker uses xUnit `[Trait("Category", "...")]` attributes to classify tests.
This document lists every category, how to run it, and what infrastructure it needs.

## Category Reference

| Category | Count | CI-Safe? | Infrastructure Required | Makefile Target |
|---|---|---|---|---|
| `Unit` | ~2,700 | Yes | None (hermetic; may spawn owned child processes, see below) | `make test` |
| `Integration` | ~140 | Yes | Real Docker daemon (or timing-sensitive environment behavior) | `make test-integration` |
| `PodmanIntegration` | ~45 | Yes* | Podman + running machine | `make test-integration` |
| `DevLocal` | ~25 | No | Docker Swarm + local registry / manual config | `make test-devlocal` |

\* PodmanIntegration is CI-safe only when a Podman machine is pre-provisioned in the CI environment.

**Taxonomy decision (recorded):** `Category` encodes the runtime boundary that matters for
the coverage gate. `Unit` means hermetic — no container runtime, no external network — but
*may* spawn an owned child process (a fake `docker`/`podman` shell script) because those
tests exercise real CLI-driver code paths and belong in the enforced coverage floor. Such
tests carry `Requires=PosixShell` and self-skip on Windows via `Assert.Skip`. `Integration`
means a real daemon (or, rarely, OS behavior that cannot be made deterministic in-process,
tagged via `Requires`). Feature labels belong in `Area`; environment needs belong in
`Requires`. The coverage floors in `make check` are measured on `Category=Unit` only.

> Counts drift as tests move. Refresh by running
> `dotnet test --list-tests --filter "Category=Unit"` (or the appropriate category).
> Feature labels belong in `Area`; environment needs belong in `Requires`.

## Running Tests

### Unit tests (CI default)

```bash
make test
# equivalent to: dotnet test --filter "Category=Unit"
```

### Docker + Podman integration subset

```bash
make test-integration
# runs ONLY Category=Integration and Category=PodmanIntegration.
# DevLocal and DMR are separate/manual.
```

`make test-integration` filters `Category=Integration|Category=PodmanIntegration`, so it
does **not** cover `DevLocal` (Swarm + registry/manual config) or Docker Model
Runner tests that also carry `Requires=Dmr`.

### A single category

```bash
dotnet test --filter "Category=PodmanIntegration"
dotnet test --filter "Area=Regression"
```

### Combining categories

```bash
# Unit + Integration only
dotnet test --filter "Category=Unit|Category=Integration"

# Everything except DevLocal
dotnet test --filter "Category!=DevLocal"
```

### DevLocal tests (Swarm + registry)

```bash
# 1. Start infrastructure
make devlocal-setup

# 2. Run DevLocal tests
make test-devlocal

# 3. Tear down infrastructure
make devlocal-teardown
```

`devlocal-setup` initialises Docker Swarm mode and starts the Podman machine.
`devlocal-teardown` leaves Swarm and stops the Podman machine.

### Cleanup stale test resources

```bash
make cleanup-test-resources
# removes leftover containers, networks, and volumes from previous test runs
```

## Release verification

No single command runs "everything". Before a release, run the categories that match
what changed — each is a distinct gate with its own infrastructure:

| Gate | Command | Infrastructure | When to run |
|---|---|---|---|
| Build + unit | `make test` (net10.0) + `make test-net8` | None | Every change (CI runs this) |
| Lint / format | `make lint` | None | Every change |
| Pre-push gate | `make check` | None | Runs lint, unit, adapter runners, coverage |
| Docker + Podman integration | `make test-integration` | Docker daemon; Podman machine for `PodmanIntegration` | Any driver/service/lifecycle change |
| Coverage floor | `make coverage-check` | None | Before merge/release |
| Docker Model Runner | `make test-dmr` (`FLUENTDOCKER_REQUIRE_DMR=1`) | Docker Model Runner runtime | Model Runner / inference changes |
| DevLocal (Swarm + registry) | `make devlocal-setup && make test-devlocal && make devlocal-teardown` | Docker Swarm + local registry | Swarm/stack or registry changes |
| Manual requirements | `dotnet test --filter "Requires=ManualOnly|Requires=LongRunning"` | Podman machine / manual config | On demand, before a tagged release |

CI runs build + unit on every push and gates the Docker/Podman/DMR suites behind PR
label, schedule, or manual dispatch — a green CI badge alone does **not** prove the
integration or DMR gates ran.

## Trait Usage Patterns

Tests use class-level traits for the primary category and may add secondary traits:

```csharp
[Trait("Category", "Integration")]
[Trait("Area", "WaitCondition")]
public class WaitConditionTests { }
```

When filtering, secondary traits allow finer-grained selection:

```bash
dotnet test --filter "Area=WaitCondition"
```

## Adding a New Category

1. Pick one existing category: `Unit`, `Integration`, `PodmanIntegration`, or `DevLocal`.
2. Put feature labels in `[Trait("Area", "...")]`.
3. Put environment gates in `[Trait("Requires", "...")]`.
4. Add setup/teardown instructions if new infrastructure is needed.
