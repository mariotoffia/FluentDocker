---
layout: default
title: ADR 0001 - Testing Core Package
parent: Testing
nav_order: 8
---

# ADR 0001: Keep testing core in the FluentDocker package during preview

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](https://mariotoffia.github.io/FluentDocker/getting-started.html#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

## Status

Accepted for `3.2.0-preview.2`; revisit before `3.2.0` GA.

## Context

Finding 9.6 noted that `FluentDocker/Testing/Core/*` ships inside the production `FluentDocker` package. FluentDocker is strong-named and does not use `InternalsVisibleTo`, so test-support types consumed by `FluentDocker.Testing.Xunit`, `FluentDocker.Testing.MsTest`, `FluentDocker.Testing.NUnit`, and user tests must be public.

Moving these types to a dedicated `FluentDocker.Testing` core package would reduce IntelliSense noise in the production package, but it is a package-graph and public-API split. After GA, that move would be breaking for strong-named consumers and adapter packages.

## Decision

Keep `FluentDocker.Testing.Core` public types in the `FluentDocker` package for the preview hardening pass. Do not split packages in Chunk 9.

Before `3.2.0` leaves preview, decide whether to introduce a dedicated `FluentDocker.Testing` core package and move the public testing-core API there.

## Consequences

- Production consumers see testing support types in IntelliSense.
- Strong-named public testing-core APIs become a compatibility commitment if they remain for GA.
- The preview window remains available for a package split without breaking a stable `3.2.0` release.
