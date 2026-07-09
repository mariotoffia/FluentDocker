# Development Guide

## Setup

Use the Makefile as the command surface. Run `make dep` to verify/install the required .NET SDKs on supported developer machines.

## Verification Gates

Before marking implementation work done, run:

```bash
make check
```

`make check` runs formatting/lint verification plus unit tests. For runner-specific changes, also run:

```bash
make test-runners
```

Use narrower targets only while iterating; the pre-done gate is still `make check`.

## Testing GitHub Actions Locally

Use [nektos/act](https://github.com/nektos/act) when you need to dry-run CI locally:

```bash
brew install act
act -j build
```

For local secrets, copy `.env.example` to `.env` and run:

```bash
act -j build --env-file .env
```

On Apple Silicon, force the runner architecture when needed:

```bash
act -j build --container-architecture linux/amd64
```

`act` is a CI smoke test only. Container-based tests can be unreliable in Docker-in-Docker; run the Makefile targets on a machine with Docker/Podman for final verification.

## Release Checklist

- Keep `Directory.Build.props`, README, docs, and CHANGELOG versions aligned.
- Before merging a preview branch, sweep/remove preview banners and branch-specific links.
- Package metadata uses the icon under `icon/`, per-project README files, Source Link, and the single `<Version>` in `Directory.Build.props`.
