---
title: Docker API Driver
nav_order: 13
---

# Docker API Driver (Production Notes)
{: .no_toc }

The Docker API driver talks to the Docker Engine over its HTTP(S) endpoint directly — no
`docker` CLI binary is required. Use it for locked-down hosts where you cannot shell out,
or for remote engines reached over TCP+TLS. This page covers the production concerns that
differ from the [CLI driver](containers.md).

1. TOC
{:toc}

## When to use the API driver vs the CLI driver

| Concern | API driver | CLI driver |
|---|---|---|
| Requires `docker` binary | No | Yes |
| Remote TCP + TLS engines | Yes (native) | Via `DOCKER_HOST` |
| Compose V2 / Stack | **Not supported** | Supported |
| Matches `docker` output exactly | No | Yes |

Register it on the kernel with `WithDockerApi`:

```csharp
using System;
using FluentDocker.Kernel;

await using var kernel = await FluentDockerKernel.Create()
    .WithDockerApi("api", d => d
        .AtHost("tcp://engine.internal:2376")
        .WithCertificates("/etc/docker/certs")
        .WithRequestTimeout(TimeSpan.FromMinutes(5))
        .AsDefault())
    .BuildAsync();
```

## Private registry authentication (X-Registry-Auth)

Log in once via the `IAuthDriver` port. The credentials are cached per-registry, and every
subsequent pull/push sends them in the `X-Registry-Auth` header (a base64url-encoded JSON
auth config), so **private-registry pull and push work over the API driver**.

```csharp
using FluentDocker.Drivers;
using FluentDocker.Model.Drivers;

var context = new DriverContext("api");
var auth = kernel.SysCtl<IAuthDriver>("api");

var login = await auth.LoginAsync(context, new RegistryLoginConfig
{
    Server = "registry.internal:5000",
    Username = "ci",
    Password = Environment.GetEnvironmentVariable("REGISTRY_TOKEN")
});

// login.Success == true; later image pull/push to registry.internal:5000
// automatically carry the X-Registry-Auth header.
```

Call `LogoutAsync(context, server)` to drop the cached credentials. Unlike the CLI driver,
the API driver does **not** read `~/.docker/config.json` or invoke Docker credential
helpers; call `LoginAsync` explicitly before private-registry pull/push.

## Cancellation vs request timeout

The two failure modes are kept **distinct**:

- **Caller cancellation** — cancelling the `CancellationToken` you pass surfaces as an
  `OperationCanceledException`, not a generic driver error. You can bound any operation
  from a test or request pipeline and catch `OperationCanceledException` reliably.
- **Request timeout** — the connection's own HTTP request timeout
  (`WithRequestTimeout(...)`) is internal; when it fires it is reported as a timeout/driver
  failure, **not** as caller cancellation, so you can tell "the caller gave up" apart from
  "the engine was too slow".
- **Long-running waits/streams** — attach/log/event/stat streams and `WaitAsync` are exempt
  from the request timeout and are bounded only by the caller's cancellation token.

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
try
{
    await using var results = await new Builder()
        .WithinDriver("api", kernel)
        .UseContainer(c => c.UseImage("nginx:alpine"))
        .BuildAsync(cancellationToken: cts.Token);
}
catch (OperationCanceledException)
{
    // Caller cancelled (or the 30s budget elapsed) — distinct from an engine timeout.
}
```

## Build support and build-context packaging

Image builds use the Docker Engine legacy `/build` endpoint. BuildKit-only Dockerfile
features such as `RUN --mount=...` and heredocs are not enabled by this driver; use the CLI
driver when you need `DOCKER_BUILDKIT=1` semantics.

The build context is packed by the API driver, not by the Docker CLI. File modes are preserved
where the host exposes them (falling back to `0644` files and `0755` directories/executables),
but symlink behavior differs from `docker build`: file symlinks are dereferenced only when the
resolved target stays inside the context, escaping links are skipped, and directory symlinks are
not traversed.

`CopyToAsync` also builds a tar archive client-side. It skips reparse-point entries to avoid
symlink cycles; very large directory copies are still buffered before upload, so prefer copying
files or bounded directories with the API driver.

## TLS

TLS is validated by default. Two knobs adjust it:

- **`WithTlsVerification(true)` (default)** — the server certificate chain is validated.
- **`WithAllowTlsHostnameMismatch()`** — relaxes **only** the hostname check. The chain is
  **still validated**; the callback accepts `SslPolicyErrors.None` or
  `RemoteCertificateNameMismatch` and nothing else. It applies to both the custom-CA path
  (`WithCertificates(...)`) and the system-trust path. Use it when an engine's cert is
  issued for a different SAN/CN than the address you dial, without dropping chain
  validation.
- **`WithTlsVerification(false)`** — the **only** accept-any-certificate mode. It disables
  all certificate validation; reserve it for local throwaway engines, never production.

```csharp
await using var kernel = await FluentDockerKernel.Create()
    .WithDockerApi("api", d => d
        .AtHost("tcp://10.0.0.5:2376")
        .WithCertificates("/etc/docker/certs")
        .WithAllowTlsHostnameMismatch()   // hostname-only relaxation; chain still checked
        .AsDefault())
    .BuildAsync();
```

## Event, log, and exec streams are bounded

Multiplexed log/exec/attach streams now **error on truncation** instead of silently
returning a short read: a partial or oversized frame throws a `DriverException`
(`"Docker log stream truncated…"` / `"Docker exec stream truncated…"`). Individual frames
are bounded (10 MiB max frame size), so a corrupt length prefix cannot allocate unbounded
memory.

The HTTP response backing a stream is owned by the returned stream and disposed with it —
always dispose the stream you receive (`await using`/`using`) so the underlying connection
is released.

`IContainerDriver.GetLogsAsync(follow: true)` is rejected because it is a buffered API; use
`IStreamDriver.StreamLogsAsync` for following logs. Streamed Docker API log entries are emitted
at Docker frame granularity (frames may split very long logical lines). Attach over the API
supports stdout/stderr only: requesting stdin fails with a clear error, and the returned
`OutputStream` is the raw Docker attach stream (multiplexed when TTY is disabled).

## Empty response handling

Operations that expect JSON now treat an empty successful body as a driver failure with a
domain message. Operations where Docker legitimately returns no body (for example start/stop
style endpoints) still return `Ok`.

## Unsupported / limited semantics vs the CLI driver

- **Compose V2 and Stack are CLI-only.** The API pack reports `SupportsCompose = false`;
  resolving those ports on the API driver throws `InterfaceNotSupportedException` (a *soft*
  failure — `TrySysCtl<T>` returns `false` rather than throwing). Use the CLI driver when
  you need Compose or Stack.
- Output does not byte-for-byte match the `docker` CLI; parse structured API responses
  rather than scraping CLI text.

## Related

- [Containers](containers.md) — the fluent container API (shared by both drivers)
- [Utilities](utilities.md) — endpoint resolution, command-response handling
- [Architecture](architecture.md) — kernel, driver packs, and port resolution
