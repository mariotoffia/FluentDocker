# Plan 04 — Docs & Metadata

> REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Make the high-traffic entry-point docs and package metadata accurate, so copy-paste users and changelog readers aren't misled. Do this **last**, after behavior (Plan 02) and API (Plan 03) are final.

**Architecture:** Pure documentation/metadata edits — no code, no tests beyond a link/snippet sanity check. Each is a one- or two-line correction.

## Global Constraints

See [README.md](README.md). Doc files ≤ 600 lines (`wc -l`). No code changes here.

---

## Task 1: A6 — Fix the README embedding-example model mismatch

**Files:**
- Modify: `README.md:232-244`

**Why:** Verified — the example loads `ai/smollm2` (a chat model) but calls `EmbedAsync` with `ai/embeddinggemma`; copy-paste users hit model-not-loaded/404.

- [ ] **Step 1: Make the pulled model match the embed model.** Either pull the embedding model used by `EmbedAsync`:

```csharp
.ForModel("ai/embeddinggemma")    // pull the SAME model the embed call uses
// ...
ModelReference.Parse("ai/embeddinggemma")
```
or split into a clearly-labelled "embedding setup" block that pulls `ai/embeddinggemma` before the `EmbedAsync` call. Pick whichever reads best in context; the invariant is **pull-model == embed-model**.

- [ ] **Step 2: Verify no other mismatch** — `grep -n "ai/smollm2\|ai/embeddinggemma" README.md` and confirm the embedding snippet is now self-consistent.

- [ ] **Step 3: Commit**

```bash
git commit -am "docs(readme): embedding example pulls the same model it embeds with (A6)"
```

---

## Task 2: D21 — Correct the changelog's `/models*` management claim

**Files:**
- Modify: `CHANGELOG.md:14`

**Why:** Verified — the entry claims "a **native `/models*` management adapter**", but that management API driver (`DockerApiModelRuntimeDriver` + `ModelApiPaths`) was deleted as unreachable/half-built. Only CLI management (`docker model …`) and the HTTP **inference** adapter are live.

- [ ] **Step 1: Remove/rewrite the overstated clause.** Change the sentence so it lists only what ships:

> CLI management/runtime adapters (`docker model …`) and an HTTP **inference** adapter speaking the OpenAI-compatible API on `:12434` (TCP or unix socket), with SSE streaming (`[DONE]` termination, mid-stream fault → `ModelRunnerException`, cancellation). **Model management (pull/list/remove/inspect) is CLI-based; there is no native HTTP `/models*` management API in this release.**

- [ ] **Step 2: Grep for other overstatements** — `grep -rn "/models\*\|models\* management\|management adapter" CHANGELOG.md docs/` and fix any echo of the same claim (e.g. in `docs/model-runner.md`).

- [ ] **Step 3: Commit**

```bash
git commit -am "docs(changelog): drop the non-existent native /models* management adapter claim (D21)"
```

---

## Task 3: D22 — Fix the package README link to the model-runner guide

**Files:**
- Modify: `FluentDocker/README.md:145`

**Why:** Verified — the NuGet-packaged README hardlinks `https://github.com/mariotoffia/FluentDocker/blob/master/docs/model-runner.md`, but that doc does not exist on `master` yet (master HEAD is `4aef31a`, pre-feature). A user opening the 3.2.0 package hits a 404.

**Decision basis:** The `release` job (Plan 01) publishes from `master` push and tags `RELEASE_VERSION` (e.g. `3.2.0`) at the same commit that brings `docs/model-runner.md` onto master. A package is immutable, so pin the link to the **release tag** (always resolvable once tagged) rather than a moving branch.

- [ ] **Step 1: Pin the link to the release tag**

```markdown
See the [Docker Model Runner guide](https://github.com/mariotoffia/FluentDocker/blob/3.2.0/docs/model-runner.md)
for endpoints, configuration, and advanced inference routing.
```
(Use the exact `<Version>` from `Directory.Build.props`. If the team prefers a moving link, `blob/master/...` is acceptable **only** because the doc lands on master at merge — but the tag is safer for an immutable package.)

- [ ] **Step 2: Confirm the doc path exists on the branch** — `test -f docs/model-runner.md && echo OK`. Confirm the version — `dotnet msbuild FluentDocker/FluentDocker.csproj -getProperty:Version -nologo -verbosity:quiet`.

- [ ] **Step 3: Commit**

```bash
git commit -am "docs(package): pin model-runner guide link to the release tag, not master (D22)"
```

---

## Task 4: C13 — Improve the default-endpoint failure message + quick-start docs

**Files:**
- Modify: `FluentDocker/Drivers/Models/Connection/ModelApiConnection.cs` (the `EndpointUnreachable` message, ~line 341)
- Modify: `docs/model-runner.md` (quick-start / troubleshooting)

**Why:** Verified (nuance) — the default endpoint is `localhost:12434` and `DOCKER_MODEL_RUNNER_URL` override + alternative transports already exist, but the failure message is generic ("endpoint is unreachable") and doesn't point users at the fix. This is a UX/docs issue, not a design flaw — note it's the only "code" change in this docs plan and is a message-string edit (still add a unit test if the message is asserted anywhere).

- [ ] **Step 1: Make the unreachable message actionable**

```csharp
private static ModelRunnerException EndpointUnreachable(Exception inner) =>
    new($"The model runner endpoint is unreachable ({inner.Message}). The default is host TCP " +
        $"http://localhost:12434 — ensure `docker model` is running, or set DOCKER_MODEL_RUNNER_URL, " +
        $"or pass an explicit endpoint (unix socket / container-internal). See docs/model-runner.md.",
        ErrorCodes.ModelInference.EndpointUnreachable, inner);
```
If any unit test asserts the exact old message, update it.

- [ ] **Step 2: Add a "Where is the runner?" quick-start block** to `docs/model-runner.md` listing the resolution order (env var → host TCP `:12434`) and the unix-socket / container-internal alternatives.

- [ ] **Step 3: Run tests + lint** (a string change can break an assertion). `make test && make lint`.

- [ ] **Step 4: Commit**

```bash
git commit -am "fix(model): actionable endpoint-unreachable message + quick-start docs (C13)"
```

---

## Task 5: A5 — (clarity nit) make the Examples run command explicit

**Files:**
- Modify: `Examples/README.md:55-57`

**Why:** **Refuted as a failure** — bare `dotnet run` does NOT fail; for a multi-target project it picks the first TFM (net8.0). This is a clarity improvement only, not a bug. Include it for polish.

- [ ] **Step 1: Show the explicit framework form**

```bash
dotnet run --project Examples/ModelRunner -f net10.0
```
(Replaces `cd ModelRunner && dotnet run`. Keep a note that `-f net8.0` also works.)

- [ ] **Step 2: Commit**

```bash
git commit -am "docs(examples): show explicit -f net10.0 run command (A5, clarity)"
```

---

## Self-review checklist

- [ ] A6: pulled model == embedded model in the README snippet.
- [ ] D21: no remaining claim of a native HTTP `/models*` management API (checked CHANGELOG + docs).
- [ ] D22: package README link resolves (tag-pinned or confirmed-on-master-at-merge).
- [ ] C13: message names the default, the env var, and the alternatives; tests updated if they assert it.
- [ ] A5: marked as clarity, not a fix.
- [ ] `wc -l` on every touched doc ≤ 600.
