# Test Categories & Run Guide

FluentDocker uses xUnit `[Trait("Category", "...")]` attributes to classify tests.
This document lists every category, how to run it, and what infrastructure it needs.

## Category Reference

| Category | Count | CI-Safe? | Infrastructure Required | Makefile Target |
|---|---|---|---|---|
| `Unit` | ~107 | Yes | None | `make test` |
| `Integration` | ~16 | Yes | Docker daemon | `make test-integration` |
| `PodmanIntegration` | ~8 | Yes* | Podman + running machine | `make test-integration` |
| `DevLocal` | ~6 | No | Docker Swarm + local registry | `make test-devlocal` |
| `LongRunning` | ~2 | No | Podman machine (may start/stop) | manual |
| `ManualOnly` | ~6 | No | Local registry, manual config | manual |
| `WaitCondition` | ~1 | Yes | Docker daemon | `make test-integration` |
| `Regression` | ~1 | Yes | Docker daemon | `make test-integration` |
| `MultiContainer` | ~1 | Yes | Docker daemon | `make test-integration` |
| `FluentVolume` | ~1 | Yes | Docker daemon | `make test-integration` |
| `FluentNetwork` | ~1 | Yes | Docker daemon | `make test-integration` |
| `FluentContainer` | ~1 | Yes | Docker daemon | `make test-integration` |
| `Compose` | ~1 | Yes | Docker daemon + Compose | `make test-integration` |

\* PodmanIntegration is CI-safe only when a Podman machine is pre-provisioned in the CI environment.

## Running Tests

### Unit tests (CI default)

```bash
make test
# equivalent to: dotnet test --filter "Category=Unit"
```

### All integration tests (Docker + Podman)

```bash
make test-integration
# runs ALL tests, not just Unit
```

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
