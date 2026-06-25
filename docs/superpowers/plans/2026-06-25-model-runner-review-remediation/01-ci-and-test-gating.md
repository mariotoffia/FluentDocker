# Plan 01 — CI & Test Gating

> REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Re-shape CI so unit and integration tests are label-gated, units still run on merge to main, and a real (self-hosted) DMR gate replaces the green-but-empty hosted gate. Also fix the `make lint` blocker.

**Architecture:** Split the monolithic `build` job into `build` (compile/pack, always) + `unit-tests` (gated) + `release` (master-only, needs both). Re-gate `integration-tests` on the `test-integration` label. Add a `dmr-tests` job on `[self-hosted, dmr]` gated on `test-dmr`. Model/DMR tests stay `Category=Integration`; a secondary `Requires=Dmr` trait lets the self-hosted lane target them and assert non-zero execution.

**Tech Stack:** GitHub Actions, `dotnet test --filter`, xUnit v3 traits, `act` (local), `actionlint` (yaml validation).

## Global Constraints

See [README.md](README.md) → Global constraints. Plus:
- Branches that count as "merge to main" for unit tests: the existing `on.push.branches` list — `master`, `main`, `fdv3`, `support/**`.
- Label names are exact: `test-unit`, `test-integration`, `test-dmr`.
- The `dotnet test --filter` for "unit only" is `Category=Unit` (explicit trait; units are NOT identified by absence of a trait — verified).

---

## Task 1: Fix the `make lint` whitespace blocker (A1)

**Files:**
- Modify: `FluentDocker.Tests/CoreTests/BuilderTests/BuilderModelExtensionsTests.cs:312`

**Why:** `make lint` exits 2. Verified diagnostic: `BuilderModelExtensionsTests.cs(312,54): error WHITESPACE: ... Replace 1 characters with '\n\s\s\s\s\s\s\s\s'`. The line `await using ((System.IAsyncDisposable)runner) { }` has a brace-block formatting issue at column 54.

- [ ] **Step 1: Reproduce the failure**

Run: `make lint`
Expected: exits non-zero, reports the `BuilderModelExtensionsTests.cs(312,54)` whitespace error.

- [ ] **Step 2: Auto-fix formatting**

Run: `dotnet format whitespace FluentDocker.sln` (or `make format` to also fix style).
This rewrites line 312 into the formatter's canonical layout (the empty `await using (...) { }` block onto separate indented lines).

- [ ] **Step 3: Verify lint passes**

Run: `make lint`
Expected: exits 0, no diagnostics.

- [ ] **Step 4: Verify the test still builds/passes**

Run: `dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "FullyQualifiedName~BuilderModelExtensionsTests" --framework net10.0`
Expected: PASS (formatting change is whitespace-only).

- [ ] **Step 5: Commit**

```bash
git add FluentDocker.Tests/CoreTests/BuilderTests/BuilderModelExtensionsTests.cs
git commit -m "style: fix whitespace lint failure in BuilderModelExtensionsTests"
```

---

## Task 2: Add a `Requires=Dmr` trait to the DMR/model integration tests

**Files:**
- Modify: the DMR/model integration test classes (currently carry only `[Trait("Category", "Integration")]`). Find them: `grep -rln "Category\", \"Integration" FluentDocker.Tests/Integration | xargs grep -l -i "ModelRunner\|docker model\|dmr"`. The known one is `FluentDocker.Tests/Integration/.../ModelRunnerIntegrationTests.cs`.

**Why:** Q3 — model tests stay `Category=Integration` (no new top-level category). But the self-hosted gate (Task 6) must run *only* the DMR tests and assert they actually executed. A second overlay trait `Requires=Dmr` makes them selectable without removing them from the `test-integration` lane. This mirrors how the repo already overlays secondary traits (`WaitCondition`, `Regression`, etc.).

**Interfaces:**
- Produces: every DMR test class is selectable via filter `Category=Integration&Requires=Dmr`, and still matched by `Category=Integration`.

- [ ] **Step 1: Add the overlay trait to each DMR test class**

For every DMR/model-runner integration test class, add a second trait alongside the existing one:

```csharp
[Trait("Category", "Integration")]
[Trait("Requires", "Dmr")]
public class ModelRunnerIntegrationTests
{
    // ...
}
```

- [ ] **Step 2: Verify the filter selects only DMR tests (list, don't run)**

Run: `dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --framework net10.0 --filter "Category=Integration&Requires=Dmr" --list-tests`
Expected: lists only the DMR/model-runner test methods; no other integration tests.

- [ ] **Step 3: Verify they're still in the broad integration set**

Run: `dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --framework net10.0 --filter "Category=Integration" --list-tests`
Expected: the DMR tests still appear (so `test-integration` still covers them, self-skipping without a runner).

- [ ] **Step 4: Commit**

```bash
git add FluentDocker.Tests/Integration
git commit -m "test: tag DMR integration tests with Requires=Dmr overlay trait"
```

---

## Task 3: Add Makefile parity targets for the new lanes

**Files:**
- Modify: `Makefile` (the `test-integration` target at lines 43–47; add `test-dmr`; update `help`)

**Why:** Local commands should mirror the CI filters so contributors can reproduce each lane. Today `make test-integration` runs **all** tests (no filter) — align it to `Category=Integration` to match the CI lane, and add `make test-dmr`.

- [ ] **Step 1: Align `test-integration` and add `test-dmr`**

Replace the `test-integration` target and add `test-dmr` after it:

```makefile
.PHONY: test-integration
test-integration:
	@mkdir -p .out/test
	@rm -rf .out/test/integration-test.txt
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "Category=Integration" --configuration Debug --verbosity normal 2>&1 | tee .out/test/integration-test.txt

# Real Docker Model Runner gate. Requires a working `docker model` runtime.
# FLUENTDOCKER_REQUIRE_DMR=1 makes the DMR tests HARD-FAIL instead of self-skipping
# when the runner is missing, so a green run proves real coverage.
.PHONY: test-dmr
test-dmr:
	@mkdir -p .out/test
	@rm -rf .out/test/dmr-test.txt
	FLUENTDOCKER_REQUIRE_DMR=1 dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "Category=Integration&Requires=Dmr" --configuration Debug --verbosity normal 2>&1 | tee .out/test/dmr-test.txt
```

- [ ] **Step 2: Update the `help` target**

Add two lines to the `help` echo block (after the `test-integration` line):

```makefile
	@echo "  test-dmr         - Run real Docker Model Runner tests (requires docker model runtime)"
```
and change the `test-integration` help text to `Run integration tests (Category=Integration; requires Docker/Podman)`.

- [ ] **Step 3: Verify the targets parse and filter correctly**

Run: `make -n test-integration test-dmr`
Expected: prints the two `dotnet test` commands with the correct `--filter` strings; no execution errors.

- [ ] **Step 4: Commit**

```bash
git add Makefile
git commit -m "build: align make test-integration filter and add make test-dmr lane"
```

---

## Task 4: Split `build` → `build` + `unit-tests` + `release`; gate units

**Files:**
- Modify: `.github/workflows/ci.yml` (the `build` job, lines 26–213)

**Why:** Requirement 2 — units run on merge to main and on PRs only with `test-unit`; publishing must still wait for units to pass. The current `build` job bundles compile + units + coverage + pack + publish + tag, and runs units on every PR. Split responsibilities so units can be gated independently while compile/pack still validate every PR, and publishing depends on units passing.

**Interfaces:**
- Produces three jobs: `build` (compile/examples/pack/validate/upload — always), `unit-tests` (gated), `release` (master-only, `needs: [build, unit-tests]`).

- [ ] **Step 1: Add a `run_dmr` dispatch input** (used by Task 6)

In `ci.yml` `on.workflow_dispatch.inputs`, add after `run_integration`:

```yaml
      run_dmr:
        description: 'Run the real Docker Model Runner (self-hosted) gate'
        required: false
        type: boolean
        default: false
```

- [ ] **Step 2: Reduce the `build` job to compile + package only**

Remove from the `build` job (lines 26–213) these test/coverage/publish steps: "Run tests (in act)", "Run tests with coverage (in GitHub)", "Enforce coverage threshold", "Generate per-assembly coverage report", "Upload coverage report artifact", "Run unit tests (net8.0)", "Upload coverage reports to Codecov", "Publish NuGet packages", "Create and push Git tag for release". Keep: detect-env, checkout, setup, read version, restore, build, build examples, create/validate/list/upload NuGet packages. The trimmed `build` job runs on every push+PR across the 3-OS matrix (unchanged trigger — no `if:`).

- [ ] **Step 3: Add the gated `unit-tests` job**

Insert this job (after `build`):

```yaml
  unit-tests:
    name: Unit Tests (${{ matrix.os }})
    # Requirement 2: run on merge to main (push) always; on PRs only with the
    # `test-unit` label. workflow_dispatch is always allowed for manual runs.
    if: >
      github.event_name == 'push' ||
      github.event_name == 'workflow_dispatch' ||
      (github.event_name == 'pull_request' &&
       contains(github.event.pull_request.labels.*.name, 'test-unit'))
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest, windows-latest, macos-latest]
    runs-on: ${{ matrix.os }}
    env:
      DOTNET_NOLOGO: 'true'
      DOTNET_CLI_TELEMETRY_OPTOUT: 'true'
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x
      - run: dotnet restore
      - run: dotnet build --configuration Release --no-restore
      - name: Run unit tests with coverage (net10.0)
        run: >
          dotnet test --no-build --configuration Release --framework net10.0
          --filter "Category=Unit"
          --collect:"XPlat Code Coverage" --results-directory ./coverage
          --settings coverletArgs.runsettings
      - name: Enforce coverage threshold
        if: matrix.os == 'ubuntu-latest'
        shell: bash
        env:
          COVERAGE_REPORT_DIR: ./coverage
          COVERAGE_LINE_MIN: '70'
          COVERAGE_BRANCH_MIN: '65'
        run: bash scripts/coverage-threshold
      - name: Generate per-assembly coverage report
        if: matrix.os == 'ubuntu-latest'
        shell: bash
        run: |
          dotnet tool install -g dotnet-reportgenerator-globaltool || true
          export PATH="$PATH:$HOME/.dotnet/tools"
          reportgenerator "-reports:./coverage/**/coverage.opencover.xml" "-targetdir:./coverage/report" "-reporttypes:HtmlInline;TextSummary;Cobertura"
      - name: Upload coverage report artifact
        if: matrix.os == 'ubuntu-latest'
        uses: actions/upload-artifact@v4
        with:
          name: coverage-report
          path: |
            coverage/**/coverage.opencover.xml
            coverage/report/**
          retention-days: 7
          if-no-files-found: warn
      - name: Upload coverage reports to Codecov
        if: matrix.os == 'ubuntu-latest'
        uses: codecov/codecov-action@v5
        with:
          token: ${{ secrets.CODECOV_TOKEN }}
      - name: Run unit tests (net8.0)
        run: >
          dotnet test --no-build --configuration Release --framework net8.0
          --filter "Category=Unit"
```

> Note: the `act` local-runner branch ("Run tests (in act)") is dropped here for brevity; if local `act` runs are still used, re-add the act-detection step + `if: running_in_act == 'true'` variant exactly as in the old `build` job.

- [ ] **Step 4: Add the master-only `release` job that needs units**

```yaml
  release:
    name: Publish & Tag
    needs: [build, unit-tests]
    if: >
      github.event_name == 'push' &&
      (github.ref == 'refs/heads/master' || github.ref == 'refs/heads/main' ||
       startsWith(github.ref, 'refs/heads/support/'))
    runs-on: ubuntu-latest
    env:
      DOTNET_NOLOGO: 'true'
      DOTNET_CLI_TELEMETRY_OPTOUT: 'true'
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x
      - name: Read release version
        id: version
        shell: bash
        run: |
          VERSION=$(dotnet msbuild FluentDocker/FluentDocker.csproj -getProperty:Version -nologo -verbosity:quiet | tr -d '[:space:]')
          echo "version=$VERSION" >> "$GITHUB_OUTPUT"
      - run: dotnet restore
      - run: dotnet build --configuration Release --no-restore
      - run: dotnet pack --configuration Release --no-build --output packages
      - name: Publish NuGet packages
        run: dotnet nuget push "packages/*.nupkg" --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
      - name: Create and push Git tag for release
        env:
          RELEASE_VERSION: ${{ steps.version.outputs.version }}
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: |
          if git ls-remote --exit-code --tags origin "refs/tags/$RELEASE_VERSION" >/dev/null 2>&1; then
            echo "Tag $RELEASE_VERSION already exists on remote, skipping"
          else
            git config --global user.name "GitHub Actions"
            git config --global user.email "actions@github.com"
            git tag "$RELEASE_VERSION"
            git push origin "$RELEASE_VERSION"
          fi
```

- [ ] **Step 5: Validate the workflow YAML**

Run: `actionlint .github/workflows/ci.yml` (install via `brew install actionlint` if absent).
Expected: no errors. If `actionlint` is unavailable, validate with `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"` (syntax-only).

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: split build/unit-tests/release; gate units on test-unit label, run on push"
```

---

## Task 5: Re-gate `integration-tests` on the `test-integration` label

**Files:**
- Modify: `.github/workflows/ci.yml` (the `integration-tests` job, lines 259–340)

**Why:** Requirement 1 — integration tests must not run on every PR; only with the `test-integration` label (plus schedule/dispatch). They must also NOT run on merge to main.

- [ ] **Step 1: Replace the bare PR trigger with a label gate**

Change the job's `if:` (currently lines 267–270) to:

```yaml
    if: >
      (github.event_name == 'pull_request' &&
       contains(github.event.pull_request.labels.*.name, 'test-integration')) ||
      github.event_name == 'schedule' ||
      (github.event_name == 'workflow_dispatch' && inputs.run_integration == true)
```

- [ ] **Step 2: Update the OS-matrix conditional to match**

The matrix line (currently 265) keys "full matrix" off schedule/dispatch and otherwise ubuntu-only. Keep ubuntu-only for label-gated PR runs:

```yaml
        os: ${{ (github.event_name == 'schedule' || (github.event_name == 'workflow_dispatch' && inputs.run_integration == true)) && fromJson('["ubuntu-latest","macos-latest","windows-latest"]') || fromJson('["ubuntu-latest"]') }}
```
(unchanged — it already evaluates to `["ubuntu-latest"]` for PR events.)

- [ ] **Step 3: Update the stale "DMR release gate" comment block** (lines 251–258)

The new comment should say: real DMR coverage now lives in the dedicated `dmr-tests` job on a self-hosted runner (Task 6); this hosted lane self-skips DMR tests by design. Remove the instruction to use `workflow_dispatch run_integration=true` as the DMR gate.

- [ ] **Step 4: Confirm integration no longer runs on push**

Inspect the `if:` — it contains no `github.event_name == 'push'` branch. So merge-to-main does not trigger it. ✓ (Requirement 2.)

- [ ] **Step 5: Validate + commit**

Run: `actionlint .github/workflows/ci.yml`
```bash
git add .github/workflows/ci.yml
git commit -m "ci: gate integration-tests on test-integration label only (#4, req 1)"
```

---

## Task 6: Add the self-hosted `dmr-tests` gate (#4)

**Files:**
- Modify: `.github/workflows/ci.yml` (add a new job)

**Why:** Finding #4 — the old "must-run DMR" path targeted hosted runners where DMR self-skips, so it could go green with zero coverage. Requires a self-hosted `[self-hosted, dmr]` runner (maintainer confirmed this lane). The job runs only the DMR-tagged tests with `FLUENTDOCKER_REQUIRE_DMR=1` and **fails if zero DMR tests executed**.

**Interfaces:**
- Consumes: the `Requires=Dmr` trait (Task 2) and `run_dmr` input (Task 4, Step 1).

- [ ] **Step 1: Add the `dmr-tests` job**

```yaml
  dmr-tests:
    name: Docker Model Runner Gate (self-hosted)
    # Real DMR coverage. Runs only with the `test-dmr` PR label or a manual
    # workflow_dispatch run_dmr=true. Requires a self-hosted runner labeled `dmr`
    # with a working `docker model` runtime.
    if: >
      (github.event_name == 'pull_request' &&
       contains(github.event.pull_request.labels.*.name, 'test-dmr')) ||
      (github.event_name == 'workflow_dispatch' && inputs.run_dmr == true)
    runs-on: [self-hosted, dmr]
    env:
      DOTNET_NOLOGO: 'true'
      DOTNET_CLI_TELEMETRY_OPTOUT: 'true'
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x
      - name: Verify Docker Model Runner is present
        shell: bash
        run: |
          if ! docker model status > /dev/null 2>&1; then
            echo "::error::This self-hosted runner has no working 'docker model' runtime."
            exit 1
          fi
      - run: dotnet restore
      - run: dotnet build --configuration Debug --no-restore
      - name: Run real DMR tests (hard-fail on skip)
        env:
          FLUENTDOCKER_REQUIRE_DMR: '1'
        run: >
          dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj
          --configuration Debug --no-build --verbosity normal
          --filter "Category=Integration&Requires=Dmr"
          --logger "trx;LogFileName=dmr.trx" --results-directory ./dmr-results
        timeout-minutes: 30
      - name: Assert at least one DMR test actually executed
        shell: bash
        run: |
          TRX=$(ls ./dmr-results/dmr.trx 2>/dev/null | head -1)
          if [ -z "$TRX" ]; then
            echo "::error::No DMR trx produced — the DMR lane ran zero tests."; exit 1
          fi
          # Counters total/passed in the trx ResultSummary; 0 executed => empty gate.
          EXECUTED=$(grep -oE 'executed="[0-9]+"' "$TRX" | grep -oE '[0-9]+' | head -1)
          echo "DMR tests executed: ${EXECUTED:-0}"
          if [ "${EXECUTED:-0}" -lt 1 ]; then
            echo "::error::DMR gate executed 0 tests — coverage is empty, failing."; exit 1
          fi
```

> The "executed" guard is the teeth of finding #4: even with the runner present, if the filter matched nothing (e.g. a future refactor drops the `Requires=Dmr` trait), the gate fails loud instead of green.

- [ ] **Step 2: Validate the workflow**

Run: `actionlint .github/workflows/ci.yml`
Expected: no errors. (`actionlint` does not require the self-hosted runner to exist; it only checks syntax/expressions.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add self-hosted [self-hosted,dmr] gate that fails on zero DMR tests (#4)"
```

---

## Task 7: Document the new gating for contributors

**Files:**
- Modify: `docs/model-runner.md` (the Testing section) and/or `docs/test-categories.md`

**Why:** The label semantics are non-obvious. Document: `test-unit` runs units on a PR (units always run on merge to main); `test-integration` runs hosted integration (DMR self-skips); `test-dmr` runs the real DMR gate on the self-hosted runner. Note the `Requires=Dmr` overlay trait and the `make test-dmr` local equivalent.

- [ ] **Step 1: Add a "CI test lanes" subsection** describing the three labels, the merge-to-main behavior, and the local `make` equivalents (`make test`, `make test-integration`, `make test-dmr`).

- [ ] **Step 2: Verify doc length**

Run: `wc -l docs/model-runner.md`
Expected: ≤ 600 lines. If it would exceed, put the section in `docs/test-categories.md` instead.

- [ ] **Step 3: Commit**

```bash
git add docs/
git commit -m "docs: document test-unit/test-integration/test-dmr CI lanes"
```

---

## Self-review checklist

- [ ] Req 1: `integration-tests.if` has no `pull_request`-without-label and no `push` branch. ✓
- [ ] Req 2: `unit-tests.if` includes `push` (merge to main) and PR-with-`test-unit`; `integration-tests` excludes `push`. ✓
- [ ] Req 3: no new `Category=Model`; DMR tests are `Category=Integration` + `Requires=Dmr`. ✓
- [ ] #4: `dmr-tests` runs on `[self-hosted, dmr]` and fails on zero executed. ✓
- [ ] A1: `make lint` exits 0. ✓
- [ ] `release` job `needs: [build, unit-tests]` so publish waits for units. ✓
- [ ] `actionlint` clean.
