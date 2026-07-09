---
title: Podman
nav_order: 15
---

# Podman Production Notes

FluentDocker drives Podman through the same fluent API as Docker. This page documents the
runtime-specific behavior you should understand before relying on Podman in CI or
production. For a runnable sample see the [Podman quick start](index.md#podman-container-runtime).

## On this page

- [Supported Podman versions](#supported-podman-versions)
- [Not supported on Podman](#not-supported-on-podman)
- [Machines: macOS / Windows vs Linux](#machines-macos--windows-vs-linux)
- [Machine naming — the default machine, not "default"](#machine-naming--the-default-machine-not-default)
- [Readiness wait](#readiness-wait)
- [Cancellation of long operations](#cancellation-of-long-operations)
- [Progress callbacks](#progress-callbacks)
- [Remote TLS verification](#remote-tls-verification)
- [Health checks](#health-checks)
- [Standard output caps](#standard-output-caps)
- [Related](#related)

## Supported Podman versions

FluentDocker targets Podman CLI **4.x and 5.x**. Podman **4.1+** is required
for `podman kube play` (`4.0` used `podman play kube`). Podman 5.x is required
when `MachineInitConfig.Image` emits `podman machine init --image` (Podman 4
used `--image-path`; FluentDocker does not retry that legacy spelling), and
Podman **5.1+** is required for `podman update --restart`. Avoid those options
on older 4.x/5.0 clients.

## Not supported on Podman

Podman driver packs do **not** register Docker Compose, Swarm service, or Stack
drivers. Requesting those interfaces for a Podman driver id fails with
`InterfaceNotSupportedException`; use Docker for those APIs or call Podman's
own tooling outside FluentDocker.

## Machines: macOS / Windows vs Linux

Podman runs containers inside a `podman machine` VM on **macOS and Windows**, but runs
**rootless directly on the host** on **Linux** — no machine is required there.

FluentDocker only manages the machine (start + readiness) on macOS/Windows. Requesting
machine management on Linux throws a helpful error rather than silently doing nothing
(`PodmanCliDriverPack.MachineManagementApplies()` is `FdOs.IsOsx() || FdOs.IsWindows()`).

If your system installs the `podman-docker` shim, a `docker` command may actually run
Podman. FluentDocker treats `WithDockerCli(...)` as Docker and registers Docker-only
interfaces such as Swarm stack/service drivers, so prefer `WithPodmanCli(...)` when the
runtime is Podman even if the command name is `docker`.

```csharp
using FluentDocker.Kernel;

// macOS/Windows: opt into auto-start of the podman machine.
await using var kernel = await FluentDockerKernel.Create()
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
await using var kernel = await FluentDockerKernel.Create()
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

Auto-start runs only when the Podman driver pack is initialized. If the VM dies later in
the same kernel session, commands surface their normal Podman failures; create a new
kernel/driver pack to run the auto-start readiness path again.

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

| Operation | Docker CLI | Podman CLI |
|---|---|---|
| Image pull / push / build progress callbacks | Reports parsed progress events where the CLI emits them. | Accepted for API compatibility but not reported; inspect the command result/output instead. |

## Remote TLS verification

`DriverContext.VerifyTls` and `DriverContext.CertificatePath` are **not** honored by the Podman
CLI driver. Podman exposes no Docker-style daemon TLS flags (`--tlsverify`, `--tlscacert`,
`--tlscert`, `--tlskey`), and podman's own `--tls-verify` is a per-command *registry* flag, not a
connection setting, so it cannot substitute for daemon verification. Setting either property logs
a one-time warning. To reach a remote Podman securely, use an SSH connection (`ssh://…`) configured
via `podman system connection`.

## Health checks

`HealthCheckConfig.Test` exec-form `CMD` values are emitted through Podman's
string-only `--health-cmd`, so they still require a shell in the image. Use a
shell-compatible health command or avoid CLI health checks for shell-less images.

## Standard output caps

Podman output is bounded so a chatty command can never exhaust memory:

- **Non-detached `run`** (foreground) returns a **256 KiB rolling tail** of combined
  output with a visible `[FluentDocker: output truncated, ...]` marker when the head was
  discarded. Detached runs return the container id as usual.
- **`exec` and `machine ssh`** use the same rolling-tail behavior for stdout/stderr, with
  the same visible truncation marker when output exceeds the retained tail.
- **"Unbounded" long operations** stream line-by-line and keep only a bounded tail for
  error reporting, instead of failing once output crosses 4 MiB.
- **Bounded (non-streaming) commands** still cap at 4 MiB (`MaxNonStreamingOutputBytes`).

If you need the full log of a long-running container, attach or stream logs rather than
relying on the captured `run` output.

Buffered `GetLogsAsync` combines both process pipes because Podman can write container
logs to stdout and stderr. That can also include Podman's own stderr diagnostics or a
FluentDocker truncation marker; use `IStreamDriver.StreamLogsAsync` for a line stream
(stderr lines are prefixed with `[stderr] `), or `StreamLogEntriesAsync` for structured
`LogEntry` items with an explicit `Stdout`/`Stderr` source. `podman logs` has no
`--details` flag, so `StreamLogsConfig.Details` is silently ignored on Podman (Docker-only).

## Related

- [Podman quick start](index.md#podman-container-runtime)
- [Utilities](utilities.md) — sudo mechanism, endpoint resolution, resource extraction
- [Test categories](testing/test-categories.md) — the `PodmanIntegration` category and release gates
