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

> **⚠️ Do not persist long-lived production credentials in `.env`.** `.env` is git-ignored (and
> must stay so), but it still sits in cleartext in your worktree where any local tooling can read
> it. A live `NUGET_API_KEY` can publish/unlist any package the account owns and a `GITHUB_TOKEN`
> PAT can act on the repo — a real supply-chain blast radius. Prefer short-lived, minimally-scoped
> tokens supplied from an OS keychain or an `act --secret-file` kept **outside** the repo tree, and
> rotate any token that has been written to disk on a shared machine (PKG-1).

On Apple Silicon, force the runner architecture when needed:

```bash
act -j build --container-architecture linux/amd64
```

`act` is a CI smoke test only. Container-based tests can be unreliable in Docker-in-Docker; run the Makefile targets on a machine with Docker/Podman for final verification.

## Release Checklist

Releases are tag-driven: pushing the version tag runs the `release` job in
`.github/workflows/ci.yml`. The `nuget-release` environment approval gates the whole
release job; once approved it runs tag-verify → doc gate → build → pack → package-set
validation → NuGet push → GitHub Release (after build, unit tests, and the ubuntu
integration lane passed).

**Tag scheme.** The canonical release tag is `v<Version>` (for example `v3.2.0`,
`v3.2.0-preview.2`). Historical pre-3.2 tags are unprefixed (`2.85.0`, `3.1.0`); from
3.2.0 on, only `v`-prefixed tags release. Pushing an old-style bare tag (`3.2.0`) does
not silently do nothing — the release job starts and fails loudly at the
"Verify tag matches <Version>" step, which requires the tag to equal `v<Version>` from
`Directory.Build.props`.

**Sequencing — merge first, sweep, then tag.** The packed READMEs and the docs site
link `blob/master/docs/...`, and GitHub Pages deploys only on push to master. Tagging
from a feature branch would bake 404 links into the NuGet pages. In order:

1. Bump `<Version>` in `Directory.Build.props` (single source of truth) and update
   `CHANGELOG.md` (move `[Unreleased]` content under the new version).
2. Sweep the docs: remove preview banners, replace branch-specific links with permalinks.
   `make check-release-docs` must pass locally — it fails on temporary branch links, on
   stale preview version literals, and (for a GA version) on any surviving `-preview.`
   marker. The same gate runs in the release job, so an unswept doc set blocks the publish.
3. Merge to `master` (docs site deploys via `pages.yml`).
4. Tag the merge commit `v<Version>` and push the tag. Approve the `nuget-release`
   environment when prompted.
5. The job publishes the four packages (`FluentDocker`, `FluentDocker.Testing.Xunit`,
   `.MsTest`, `.NUnit`) and creates the GitHub Release with generated notes
   automatically (pre-release flag inferred from a `-` in the version).

**Package/version invariants.**

- Keep `Directory.Build.props`, README, docs, and CHANGELOG versions aligned; nothing
  else declares a version.
- Package metadata uses the icon under `icon/`, per-project README files, Source Link,
  embedded `.pdb` symbols (set once in `Directory.Build.props`), and deterministic CI
  builds (`ContinuousIntegrationBuild` when `CI=true`).
- The build and release jobs assert the package set is exactly the four expected ids —
  a new packable project fails CI until the lists are updated deliberately.

**Strong naming.** Assemblies are strong-named for identity only. On non-Windows
builds (including the ubuntu release lane) `PublicSign=true` public-signs with
`keypair.snk`; the runtime never validates strong-name signatures on .NET (Core), so
this provides a stable identity, not integrity. A Windows-built assembly would be
fully signed and thus differ byte-wise from CI output — official packages come only
from the CI release lane.
