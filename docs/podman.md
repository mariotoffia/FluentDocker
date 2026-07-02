---
title: Podman
nav_order: 6
---

# Podman Production Notes
{: .no_toc }

FluentDocker drives Podman through the same fluent API as Docker. This page documents the
runtime-specific behavior you should understand before relying on Podman in CI or
production. For a runnable sample see the [Podman quick start](index.md#podman-container-runtime).

1. TOC
{:toc}

## Machines: macOS / Windows vs Linux

Podman runs containers inside a `podman machine` VM on **macOS and Windows**, but runs
**rootless directly on the host** on **Linux** — no machine is required there.

FluentDocker only manages the machine (start + readiness) on macOS/Windows. Requesting
machine management on Linux throws a helpful error rather than silently doing nothing
(`PodmanCliDriverPack.MachineManagementApplies()` is `FdOs.IsOsx() || FdOs.IsWindows()`).

```csharp
using FluentDocker.Kernel;

// macOS/Windows: opt into auto-start of the podman machine.
using var kernel = await FluentDockerKernel.Create()
    .WithPodmanCli("podman", d => d
        .WithAutoStartMachine()
        .AsDefault())
    .BuildAsync();

// Linux: podman is rootless on the host — omit WithAutoStartMachine().
```

## Machine naming — the *default* machine, not "default"

When you do not name a machine, FluentDocker selects Podman's **actual default machine**
(the entry flagged `Default` in `podman machine list`), falling back to the first machine
if none is flagged. It does **not** invent, look up, or start a machine literally named
`"default"`.

```csharp
using FluentDocker.Kernel;

// Auto-start a specific machine by name:
using var kernel = await FluentDockerKernel.Create()
    .WithPodmanCli("podman", d => d
        .WithAutoStartMachine(cfg =>
        {
            cfg.MachineName = "podman-machine-default"; // null => Podman's default machine
            cfg.CreateIfNotExists = true;               // provision one if missing
            cfg.InitCpus = 2;
            cfg.InitMemoryMiB = 2048;
        })
        .AsDefault())
    .BuildAsync();
```

`CreateIfNotExists` provisions a machine (using the `Init*` sizing options) only when no
matching machine exists; when `false` (default) a missing machine is an error.

## Readiness wait

After starting a machine, FluentDocker **polls** `podman info` / ping until the machine
answers before running your first command, so operations do not race a half-started VM.
The wait is bounded by a timeout — if the machine never becomes ready the initialization
fails instead of hanging.

## Cancellation of long operations

Long-running operations (image pulls, machine start, `kube play`) honor the
`CancellationToken` you pass. Cancelling surfaces as `OperationCanceledException`, so you
can bound them from tests or request pipelines:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
await using var results = await new Builder()
    .WithinDriver("podman", kernel)
    .UseContainer(c => c.UseImage("nginx:alpine").ExposePort("80"))
    .BuildAsync(cancellationToken: cts.Token);
```

## Progress callbacks

Some operations accept progress callbacks. On certain code paths progress may not be
reported even though the operation completes normally. Treat progress as advisory — do not
gate control flow on receiving progress events.

## Standard output caps

Podman output is bounded so a chatty command can never exhaust memory:

- **Non-detached `run`** (foreground) returns a **256 KiB rolling tail** of combined
  output with `\n`-normalized line endings — enough to keep the trailing result line plus
  ample error context. (Earlier builds buffered up to 4 MiB.) Detached runs return the
  container id as usual.
- **"Unbounded" long operations** stream line-by-line and keep only a bounded tail for
  error reporting, instead of failing once output crosses 4 MiB.
- **Bounded (non-streaming) commands** still cap at 4 MiB (`MaxNonStreamingOutputBytes`).

If you need the full log of a long-running container, attach or stream logs rather than
relying on the captured `run` output.

## Related

- [Podman quick start](index.md#podman-container-runtime)
- [Utilities](utilities.md) — sudo mechanism, endpoint resolution, resource extraction
- [Test categories](test-categories.md) — the `PodmanIntegration` category and release gates
