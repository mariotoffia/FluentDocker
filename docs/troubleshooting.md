---
layout: default
title: Troubleshooting
nav_order: 15
---

# Troubleshooting

Start from the exception type and `DriverException.ErrorCode`. Stable codes live in
`FluentDocker.Model.Drivers.ErrorCodes`; raw daemon text can vary by Docker/Podman
version.

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
  the mapped port with `container.ToHostExposedEndpoint("80/tcp")`.

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
