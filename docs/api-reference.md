---
layout: default
title: API Reference
nav_order: 19
---

# API Reference

The type-level API reference is generated from the XML doc comments on every
public type in `FluentDocker`. It is not committed to the repository: the docs
CI build runs `xmldocmd` against the compiled assembly and writes the pages under
`api-reference/` on each deploy.

> **Preview docs — not on NuGet yet.** These document the upcoming **3.2.0-preview.2** API; build
> it from source — see [Consume the preview](https://mariotoffia.github.io/FluentDocker/getting-started.html#consume-the-preview). The latest published package
> is **3.1.0**, whose `WithPort` is container-first (host-first in the preview) — don't run these samples against it.

- [Browse the generated API reference](https://mariotoffia.github.io/FluentDocker/api-reference/FluentDocker.html)

Every generated page is indexed by the site search box at the top of the page, so
you can jump straight to a type by name. The pages stay out of the left navigation
tree — there are hundreds of them — so use search or the link above to reach one.

For task-oriented guides start from [Getting Started](getting-started.md) or the
[documentation map](index.md). Driver capabilities that the reference lists only as
interfaces — stacks, services, pods, manifests, machines, streaming, system prune —
have worked examples in [Advanced Drivers](advanced-drivers.md).
