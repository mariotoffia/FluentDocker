---
layout: default
title: Service Lifecycle
nav_order: 20
description: "Service running state, StateChange events, and lifecycle hooks in FluentDocker"
---

# Service Lifecycle: State Changes and Hooks

Every FluentDocker service — container, network, volume, compose project, image, pod,
model — implements `IServiceAsync`. That interface carries the running state, a
`StateChange` event, and a hook API you can use to react when a service starts, stops,
pauses, or is removed.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](https://mariotoffia.github.io/FluentDocker/getting-started.html#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Step by Step

- Basics: [Running state](#running-state), [StateChange event](#statechange-event)
- Hooks: [Adding hooks](#adding-hooks), [Generated hook names](#generated-hook-names)
- Guarantees: [Handler and hook isolation](#handler-and-hook-isolation), [Failure transitions to Unknown](#failure-transitions-to-unknown), [Disposal is time-bounded](#disposal-is-time-bounded)

## Running state

`IServiceAsync.State` reports the current `ServiceRunningState`. The enum has eight
values:

| State | Value | Meaning |
|---|---|---|
| `Unknown` | 0 | State has not been queried, or a lifecycle operation failed. |
| `Starting` | 1 | Service is starting. |
| `Running` | 2 | Service is running. |
| `Paused` | 3 | Service is paused and can be resumed. |
| `Stopping` | 4 | Service is stopping. |
| `Stopped` | 5 | Service is stopped but not removed. |
| `Removing` | 6 | Service is being removed. |
| `Removed` | 7 | Service has been removed. |

The lifecycle methods drive the transitions:

```text
StartAsync:   Unknown/Stopped ─▶ Starting ─▶ Running
PauseAsync:   Running ─▶ Paused
StopAsync:    Running ─▶ Stopping ─▶ Stopped
RemoveAsync:  Stopped ─▶ Removing ─▶ Removed
Start/Stop/Kill/Remove failure: ─▶ Unknown
Pause failure:                  Container/Compose ─▶ Unknown
Unpause failure:                Container/Compose ─▶ Unknown
```

## StateChange event

`StateChange` fires when the running state changes. The built-in services raise it only
on an actual transition — writing the same state again does not re-raise the event.

The handler signature is the `ServiceDelegates.StateChange` delegate,
`void (object sender, StateChangeEventArgs evt)`. `StateChangeEventArgs` carries the
service and its new state.

```csharp
using System;
using System.Linq;
using FluentDocker.Builders;
using FluentDocker.Services;

await using var results = await new Builder()
    .WithinDockerCli("docker", kernel) // kernel: a FluentDockerKernel you built earlier
    .UseContainer(c => c.UseImage("nginx:alpine").ExposePort("80"))
    .BuildAsync();

var container = results.Containers.First();

// Attached after BuildAsync, so it observes later transitions such as the
// stop/remove that happens on disposal.
container.StateChange += (sender, evt) =>
    Console.WriteLine($"{evt.Service.Name} -> {evt.State}");
```

`StateChangeEventArgs` exposes:

- `Service` — the `IServiceAsync` whose state changed.
- `State` — the new `ServiceRunningState`.

## Adding hooks

A hook is a `Func<IServiceAsync, Task>` that runs when the service reaches a given state.
`AddHook` returns the service, so registrations chain. When multiple hooks target the
same state, their execution order is unspecified.

```csharp
container
    .AddHook(ServiceRunningState.Running, async svc =>
    {
        await WarmCacheAsync(svc);
    })
    .AddHook(ServiceRunningState.Stopped, async svc =>
    {
        await FlushMetricsAsync(svc);
    });
```

Pass a `uniqueName` when you need to remove the hook later. `RemoveHook` takes that name
and also returns the service:

```csharp
container.AddHook(
    ServiceRunningState.Running,
    async svc => await WarmCacheAsync(svc),
    uniqueName: "warm-cache");

container.RemoveHook("warm-cache");
```

Without a `uniqueName`, `AddHook` generates one internally: the hook runs, but you cannot
target it for removal.

## Generated hook names

When you want a removable hook but don't want to invent a name, use
`AddHookWithGeneratedName` (an extension in `FluentDocker.Services`). It generates the
name, registers the hook, and returns the name so you can remove it later.

```csharp
using FluentDocker.Services;

string hookName = container.AddHookWithGeneratedName(
    ServiceRunningState.Stopped,
    async svc => await FlushMetricsAsync(svc));

// later
container.RemoveHook(hookName);
```

## Handler and hook isolation

State-change handlers and hooks are isolated from lifecycle operations. A handler or hook
that throws is logged and swallowed — the exception never propagates into `StartAsync`,
`StopAsync`, or the other lifecycle calls. A broken hook cannot fail a start or leave a
service half-stopped.

> **Note:** Because exceptions are swallowed, a hook that fails leaves no trace beyond the
> log entry. Catch and handle errors inside the hook if you depend on its work.

## Failure transitions to Unknown

When a lifecycle operation (start, stop, kill, remove) fails, the service transitions to
`ServiceRunningState.Unknown` and then throws. `Unknown` means the real state could not be
confirmed — the daemon may have applied the operation partially. `ContainerService.PauseAsync`,
`ComposeService.PauseAsync`, `ComposeService.UnpauseAsync`, and `ContainerService.UnpauseAsync`
also transition to `Unknown` on failure and then rethrow — `UnpauseAsync` follows the same
`Unknown`-then-throw contract as every sibling lifecycle method.

> **Warning:** Treat `Unknown` as "state not confirmed", not "nothing happened". After a
> failure, inspect or re-query the service before assuming it is safe to retry.

## Disposal is time-bounded

Disposing a container service stops and removes the container as best-effort cleanup. That
cleanup is budgeted: `ContainerService.DefaultDisposeCleanupTimeoutMs` is `30_000`
(30 seconds). If the daemon is unresponsive, disposal abandons the cleanup once the budget
elapses instead of hanging. The budget is adjustable per instance through the
constructor's `disposeCleanupTimeout` parameter.

### Disposing `BuildResults`

Disposing the `BuildResults` returned by `Builder.BuildAsync()` tears down every service it
built, but the guarantee is **time-bounded and best-effort**, not unconditional:

- **Per-service budget.** Each service gets `BuildResults.DefaultDisposeBudgetMs` (**60 s**) of
  its own, so one hung daemon call cannot starve the teardown of later resources. This budget
  applies to **both** `await using` (`DisposeAsync`) and the synchronous `using`/`Dispose()` path;
  the sync path can only bound the *caller's* wait, so a timed-out service is retained for retry
  while its abandoned dispose finishes in the background. Prefer `await using` — only there is the
  teardown actually cancellable.
- **Partial teardown & retry.** A service that fails or times out is **not** silently forgotten: it
  stays observable in `results.All`, and a **second** `Dispose`/`DisposeAsync` retries it. A second
  call that arrives while the first teardown is still running blocks on an internal gate and returns
  only once that teardown completes (it never returns early reporting success before the containers
  are gone).
- **`cleanupTimeout`.** `Builder.BuildAsync(TimeSpan? cleanupTimeout, …)` bounds the cleanup that
  runs when a build *fails* mid-way (default 120 s); the reverse-order removal manifest is attached
  to the thrown exception's `Data`.

With a wedged daemon, teardown is abandoned after the per-service budget: leaked containers then
carry the `fluentdocker.session`/`fluentdocker.managed` labels and are reclaimed by the next run's
orphan sweep or a manual `docker rm -f` by label.

## See also

- [Architecture](architecture.md) — the kernel/driver model behind services.
- [Containers](containers.md) — container-specific lifecycle and waits.
- [Testing Core](testing/core.md) — resource lifecycle in tests.
