---
layout: default
title: Troubleshooting
nav_order: 14
---

# Troubleshooting

Start from the exception type and `DriverException.ErrorCode`. Stable codes live in
`FluentDocker.Model.Drivers.ErrorCodes`; raw daemon text can vary by Docker/Podman
version.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](https://mariotoffia.github.io/FluentDocker/getting-started.html#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Docker daemon not running or unreachable

- **Symptom:** `Cannot connect to the Docker daemon`, `connection refused`, or
  `ErrorCodes.Api.ConnectionFailed` / `ErrorCodes.Driver.NotAvailable`.
- **Cause:** Docker Desktop/Engine is stopped, `DOCKER_HOST` points at the wrong daemon,
  or the remote daemon is blocked by firewall/TLS.
- **Fix:** start Docker Desktop/Engine, run `docker info`, then verify the same host
  setting used by FluentDocker. For Docker API, check `.AtHost(...)`, certs, and TCP
  reachability.

## Socket permission denied

- **Symptom:** `permission denied` while opening `/var/run/docker.sock` or running the
  `docker` CLI.
- **Cause:** the current user cannot access the Docker socket.
- **Fix:** add the user to the `docker` group and log in again, run rootless Docker, or
  configure sudo with `.WithSudo(...)` (`SudoMechanism` is experimental).

## Port already allocated

- **Symptom:** `port is already allocated`, `bind: address already in use`, usually under
  `ErrorCodes.Container.CreateFailed` / `ErrorCodes.Driver.CommandExecutionFailed`.
- **Cause:** another process/container owns the host port.
- **Fix:** remove the fixed host port and let Docker assign one, or stop the owner. Read
  the mapped port with `await container.ToHostExposedEndpointAsync("80/tcp")`.

## Container name already in use

- **Symptom:** `Conflict. The container name "..." is already in use` or similar daemon
  text under `ErrorCodes.Container.CreateFailed`.
- **Cause:** a previous run left a container with the same `.WithName(...)`.
- **Fix:** choose unique test names, remove the old container, or set
  [`ReuseIfExists()` / `DestroyIfExists(...)`](containers.md#container-existence-behavior)
  when fixed names are intentional.

## Image architecture mismatch on Apple Silicon

- **Symptom:** `exec format error`, `no matching manifest for linux/arm64`, or an image
  that starts on Intel but fails on M1/M2/M3.
- **Cause:** the image lacks a compatible architecture variant.
- **Fix:** prefer multi-arch images; otherwise set
  [`WithPlatform("linux/amd64")`](containers.md#advanced-container-options) on the
  container or pull/build the image with Docker's `--platform` option.

## WaitForPort / WaitForHealthy timeout

- **Symptom:** wait timeout (`ErrorCodes.General.Timeout`, `ErrorCodes.Network.Timeout`,
  or `ErrorCodes.Container.WaitFailed`).
- **Cause:** the app did not bind the expected port, health checks fail, or startup is
  slower than the configured timeout.
- **Fix:** increase the wait timeout only after checking the failure details. Current
  builder wait failures attach a bounded tail at `ex.Data["ContainerLogTail"]`; plain
  `FluentDockerException` wait failures also include that tail in the message.

## Image pull authentication failure

- **Symptom:** `pull access denied`, `no basic auth credentials`, HTTP 401/403, or
  `ErrorCodes.Image.PullFailed` / `ErrorCodes.Auth.LoginFailed`.
- **Cause:** private registry credentials are missing or invalid.
- **Fix:** `docker login` for CLI drivers. For Docker API, call `IAuthDriver.LoginAsync`
  because the API driver does not read CLI credential helpers.

## Compose file not found

- **Symptom:** `no such file or directory`, `can't find a suitable configuration file`,
  or a failed compose `up` before any service starts.
- **Cause:** `.WithComposeFile(...)` is relative to the process working directory, not
  the source file containing the snippet.
- **Fix:** pass an absolute path, set the test working directory, or use the
  [multiple compose files](compose.md#multiple-compose-files) pattern with paths that
  exist from the process working directory.

## InvalidOperationException during build setup

- **Symptom:** `InvalidOperationException` before Docker is contacted.
- **Cause:** in 3.2, `BuildAsync()` with zero `Use*` calls throws, and `KernelBuilder`
  is single-use.
- **Fix:** add at least one `UseContainer`/`UseNetwork`/`UseVolume`/`UseCompose` call,
  and create a fresh `FluentDockerKernel.Create()` builder for each kernel. See the
  [3.2 changelog](https://github.com/mariotoffia/FluentDocker/blob/master/CHANGELOG.md).

## Podman machine not running

- **Symptom:** `PodmanMachineNotRunningException` with `ErrorCodes.Machine.NotRunning`
  (`MACH_010`, transient).
- **Cause:** macOS/Windows need a running `podman machine`; Linux does not use machine
  management and FluentDocker throws if you request it there.
- **Fix:** on macOS/Windows run `podman machine start` or configure
  `.WithAutoStartMachine()`. On Linux, omit machine options.

```csharp
using System;
using FluentDocker.Common;

try
{
  // Build or run Podman resources.
}
catch (PodmanMachineNotRunningException ex) when (ex.IsTransient)
{
  Console.WriteLine("Start the Podman machine and retry.");
}
```

## Docker Model Runner unreachable or wrong base path

- **Symptom:** `ErrorCodes.ModelInference.EndpointUnreachable` (`MIN_003`), HTTP 404
  mapped to `ErrorCodes.ModelInference.ModelNotLoaded`, or a 404 when using a custom
  OpenAI-compatible endpoint.
- **Cause:** DMR is disabled, host-side TCP is off, `DOCKER_MODEL_RUNNER_URL` points at
  the wrong host, or the base path is wrong (`/engines/llama.cpp/v1` for DMR vs plain
  `/v1` for vLLM/LM Studio).
- **Fix:** enable DMR and host TCP, verify
  `curl http://localhost:12434/engines/llama.cpp/v1/models`, set
  `DOCKER_MODEL_RUNNER_URL`, or use `ModelRunnerEndpoint.Raw(new Uri(".../v1"))`
  for non-DMR servers.

## Model streaming idle timeout

- **Symptom:** streaming starts then fails with `ModelRunnerException` and
  `ErrorCodes.ModelInference.Timeout` (`MIN_006`).
- **Cause:** no response header/body arrived within `StreamFirstByteTimeout`, or no
  later SSE chunk arrived within `StreamReadIdleTimeout`.
- **Fix:** lengthen the relevant `ModelApiConnectionConfig` timeout, or set it to
  `null` when the caller's `CancellationToken` is the only budget.

## TLS / CA failures

- **Symptom:** certificate validation errors or `ErrorCodes.Api.ConnectionFailed`.
- **Cause:** missing CA, client cert mismatch, hostname mismatch, or TLS disabled on the
  daemon/runner.
- **Fix:** point `.WithCertificates(...)` / `ModelApiConnectionConfig.CertificatePath`
  at `ca.pem` (and client `cert.pem`/`key.pem` for mTLS). Use
  `.WithAllowTlsHostnameMismatch()` only for hostname mismatch; avoid disabling TLS
  verification except for local throwaway environments.

## Docker API stream interrupted mid-read

- **Symptom:** an event/log/stats stream on the Docker API driver fails part-way with a
  `DriverException` carrying `ErrorCodes.Api.StreamInterrupted` (`API_STREAM_INTERRUPTED`,
  transient).
- **Cause:** an already-established stream lost its transport — a connection reset, a daemon
  restart, or a proxy dropping the long-lived connection. This is distinct from
  `ErrorCodes.Api.ConnectionFailed` (never connected) and `ErrorCodes.Api.StreamEnded` (a
  boundless stream the daemon closed cleanly, an EOF — not retryable).
- **Fix:** re-open the stream. `StreamInterrupted` is transient (`ex.IsTransient == true`),
  so a retry loop resumes after the reset.

```csharp
using System;
using FluentDocker.Common;

try
{
  // Consume an event / log / stats stream from the Docker API driver.
}
catch (DriverException ex) when (ex.IsTransient)
{
  Console.WriteLine($"Stream dropped ({ex.ErrorCode}); re-opening.");
}
```
