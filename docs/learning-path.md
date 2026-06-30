---
layout: default
title: Learning Path
nav_order: 2
description: "A progressive path from first container to advanced multi-driver patterns"
---

# Learning Path

Use this page to move through FluentDocker in layers, from simple to advanced.

## Start Here (First 30 Minutes)

1. [Install and verify prerequisites](getting-started.md#installation)
2. [Run your first container](getting-started.md#your-first-container)
3. [Add one wait strategy](getting-started.md#with-wait-strategy)
4. [Review cleanup and exception basics](getting-started.md#exception-handling)

If this is your first time with FluentDocker, finish the four steps above before opening architecture or extensibility docs.

## Layer 1: Core Usage

| Goal | Read |
|---|---|
| Understand kernel + builder basics | [Getting Started](getting-started.md) |
| Create and run containers | [Containers](containers.md) |
| Run multi-service apps | [Docker Compose](compose.md) |
| Persist or mount data | [Volumes](volumes.md) |

## Layer 2: Common Production/Test Patterns

| Goal | Read |
|---|---|
| Isolated service networking | [Networking](networking.md) |
| Build custom images | [Images](images.md) |
| Write test resources and fixtures | [Testing](testing.md) |
| Use helper APIs and diagnostics helpers | [Utilities](utilities.md) |
| Handle failures predictably | [Error Handling](architecture.md#error-handling) |

## Layer 3: Advanced Internals and Customization

| Goal | Read |
|---|---|
| Understand driver/kernel design decisions | [Architecture](architecture.md) |
| Build driver-specific extensions | [Driver Extensibility](extensibility.md) |
| Upgrade existing v2 codebases | [Migration Guide](migration.md) |
| Run local LLMs (preview) | [Model Runner](model-runner.md) |

## Suggested Reading Plans

### Application Developer

1. [Getting Started](getting-started.md)
2. [Containers](containers.md)
3. [Compose](compose.md)
4. [Volumes](volumes.md)
5. [Error Handling](architecture.md#error-handling)

### Test Engineer

1. [Getting Started](getting-started.md)
2. [Testing](testing.md)
3. [Test Categories](test-categories.md)
4. [Compose](compose.md)
5. [Networking](networking.md)

### Platform/Library Engineer

1. [Getting Started](getting-started.md)
2. [Architecture](architecture.md)
3. [Driver Extensibility](extensibility.md)
4. [Error Handling](architecture.md#error-handling)
5. [Migration Guide](migration.md)
