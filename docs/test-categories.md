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
| `Unit` | ~2,700 | Yes | None | `make test` |
| `Integration` | ~140 | Yes | Docker daemon | `make test-integration` |
| `PodmanIntegration` | ~45 | Yes* | Podman + running machine | `make test-integration` |
| `DevLocal` | ~25 | No | Docker Swarm + local registry | `make test-devlocal` |
| `LongRunning` | ~12 | No | Podman machine (may start/stop) | manual |
| `ManualOnly` | ~20 | No | Local registry, manual config | manual |
| `WaitCondition` | ~10 | Yes | Docker daemon | `make test-integration` |
| `Regression` | ~6 | Yes | Docker daemon | `make test-integration` |
| `MultiContainer` | ~10 | Yes | Docker daemon | `make test-integration` |
| `FluentVolume` | ~6 | Yes | Docker daemon | `make test-integration` |
| `FluentNetwork` | ~6 | Yes | Docker daemon | `make test-integration` |
| `FluentContainer` | ~14 | Yes | Docker daemon | `make test-integration` |
| `Compose` | ~13 | Yes | Docker daemon + Compose | `make test-integration` |

\* PodmanIntegration is CI-safe only when a Podman machine is pre-provisioned in the CI environment.

> Counts as of 2026-05-11; numbers reflect `[Fact]` + `[Theory]` occurrences in files
> carrying the matching `[Trait("Category", ...)]` attribute. Refresh by running
> `dotnet test --list-tests --filter "Category=Unit"` (or the appropriate category)
> against the test project. Some categories overlap (a test may carry both
> `Integration` and `WaitCondition`, for example).

## Running Tests

### Unit tests (CI default)

```bash
make test
# equivalent to: dotnet test --filter "Category=Unit"
```

### Docker + Podman integration subset

```bash
make test-integration
# runs ONLY Category=Integration and Category=PodmanIntegration (Docker + Podman),
# NOT the full suite. DevLocal, LongRunning, ManualOnly, and DMR are separate/manual.
```

`make test-integration` filters `Category=Integration|Category=PodmanIntegration`, so it
does **not** cover `DevLocal` (Swarm + registry), `LongRunning`, `ManualOnly`, or the
Docker Model Runner (`Requires=Dmr`) categories — run those explicitly (see below).

### A single category

```bash
dotnet test --filter "Category=PodmanIntegration"
dotnet test --filter "Category=Regression"
```

### Combining categories

```bash
# Unit + Integration only
dotnet test --filter "Category=Unit|Category=Integration"

# Everything except DevLocal and ManualOnly
dotnet test --filter "Category!=DevLocal&Category!=ManualOnly&Category!=LongRunning"
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
| Docker + Podman integration | `make test-integration` | Docker daemon; Podman machine for `PodmanIntegration` | Any driver/service/lifecycle change |
| Coverage floor | `make coverage-check` | Docker daemon | Before merge/release |
| Docker Model Runner | `make test-dmr` (`FLUENTDOCKER_REQUIRE_DMR=1`) | Docker Model Runner runtime | Model Runner / inference changes |
| DevLocal (Swarm + registry) | `make devlocal-setup && make test-devlocal && make devlocal-teardown` | Docker Swarm + local registry | Swarm/stack or registry changes |
| LongRunning / ManualOnly | `dotnet test --filter "Category=LongRunning"` (and `ManualOnly`) | Podman machine / manual config | On demand, before a tagged release |

CI runs build + unit on every push and gates the Docker/Podman/DMR suites behind PR
label, schedule, or manual dispatch — a green CI badge alone does **not** prove the
integration or DMR gates ran.

## Trait Usage Patterns

Tests use class-level traits for the primary category and may add secondary traits:

```csharp
[Trait("Category", "Integration")]
[Trait("Category", "WaitCondition")]
public class WaitConditionTests { }
```

When filtering, secondary traits allow finer-grained selection:

```bash
dotnet test --filter "Category=WaitCondition"
```

## Adding a New Category

1. Apply `[Trait("Category", "YourCategory")]` to the test class.
2. Add a row to the table above.
3. If special infrastructure is needed, add setup/teardown instructions.
4. If CI should skip it, ensure the `make test` filter excludes it.
