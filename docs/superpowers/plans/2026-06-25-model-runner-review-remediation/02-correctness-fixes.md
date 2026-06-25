# Plan 02 — Correctness Fixes

> REQUIRED SUB-SKILL: superpowers:subagent-driven-development or superpowers:executing-plans. Use superpowers:test-driven-development for every task — failing test first.

**Goal:** Fix the verified behavioral defects in the model-runner / CLI subsystem without changing the refuted ones.

**Architecture:** Each fix is independently testable with a `Category=Unit` test. Order: A3 (backward-compat) → B7/B8 (CLI) → B10 (builder) → C11/C12/C14 (API) → D19/D20 (small).

**Tech Stack:** xUnit v3 + Moq, `System.Net.Security`, `ProcessStartInfo`.

## Global Constraints

See [README.md](README.md). New tests are `[Trait("Category","Unit")]`. End each task with `make test` green. Run `wc -l` on any file you touch (≤500).

---

## Task 1: A3 — Opt-in TLS hostname-mismatch (configurable, default strict)

**Files:**
- Modify: `FluentDocker/Drivers/Models/Connection/ModelTlsValidation.cs`
- Modify: `FluentDocker/Drivers/Docker/Api/Connection/DockerApiConnectionConfig.cs` (add property)
- Modify: `FluentDocker/Drivers/Models/Connection/ModelApiConnectionConfig.cs` (add property)
- Modify: `FluentDocker/Drivers/Docker/Api/Connection/DockerApiConnection.cs:338-339` (pass flag)
- Modify: `ModelApiConnection.cs:~465` (the other call site — pass flag)
- Test: `FluentDocker.Tests/CoreTests/.../ModelTlsValidationTests.cs` (existing TLS tests live here)

**Why:** `ValidateWithCustomRoot` rejects `RemoteCertificateNameMismatch` and is wired into the **existing** `DockerApiConnection` path (verified `DockerApiConnection.cs:339`), so current IP/SAN-mismatch Docker-over-TLS users break. Decision: keep default strict, add an opt-out.

**Interfaces:**
- Produces: `ModelTlsValidation.ValidateWithCustomRoot(caCert, cert, chain, errors, bool allowHostnameMismatch = false)`; `bool AllowTlsHostnameMismatch` on both config types (default `false`).

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
[Trait("Category", "Unit")]
public void NameMismatch_IsRejected_ByDefault_ButAccepted_WhenAllowed()
{
    using var ca = BuildSelfSignedCa();        // existing test helper
    using var server = BuildCertSignedBy(ca, cn: "wrong-host");
    var chain = new X509Chain();
    var errs = SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors;

    Assert.False(ModelTlsValidation.ValidateWithCustomRoot(ca, server, chain, errs)); // strict default
    Assert.True(ModelTlsValidation.ValidateWithCustomRoot(ca, server, chain, errs, allowHostnameMismatch: true));
}
```

- [ ] **Step 2: Run it — expect compile failure** (overload doesn't exist yet).

Run: `dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "FullyQualifiedName~ModelTlsValidationTests" --framework net10.0`
Expected: build error (no 5-arg overload).

- [ ] **Step 3: Add the opt-in overload**

In `ModelTlsValidation.cs`, change the method to accept the flag and only force-reject the name mismatch when not allowed:

```csharp
public static bool ValidateWithCustomRoot(X509Certificate2 caCert, X509Certificate cert, X509Chain chain,
    SslPolicyErrors errors, bool allowHostnameMismatch = false)
{
  if (errors == SslPolicyErrors.None)
    return true;

  // A missing certificate is never acceptable.
  if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
    return false;

  var remaining = errors;
  if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
  {
    if (!allowHostnameMismatch)
      return false;                 // default: reject hostname/SAN mismatch
    remaining &= ~SslPolicyErrors.RemoteCertificateNameMismatch; // opted in: ignore it
  }

  // Only the chain-trust error is eligible for custom-root re-validation.
  if ((remaining & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
    return false;
  if (caCert == null || chain == null || cert == null)
    return false;

  chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
  chain.ChainPolicy.CustomTrustStore.Add(caCert);
  if (cert is X509Certificate2 server)
    return chain.Build(server);
  using var copy = new X509Certificate2(cert);
  return chain.Build(copy);
}
```

- [ ] **Step 4: Add the config flag to both config types**

In `DockerApiConnectionConfig.cs` and `ModelApiConnectionConfig.cs`, after `VerifyTls`:

```csharp
/// <summary>
/// When true, a TLS certificate whose hostname/SAN does not match the connection host
/// is still accepted provided the chain validates against the configured CA. Default
/// false (strict). Set true only for IP-based connections to a known host.
/// </summary>
public bool AllowTlsHostnameMismatch { get; set; }
```

- [ ] **Step 5: Thread the flag into both callbacks**

`DockerApiConnection.cs:338-339`:
```csharp
sslOptions.RemoteCertificateValidationCallback = (_, cert, chain, errors) =>
    ModelTlsValidation.ValidateWithCustomRoot(caCert, cert, chain, errors, config.AllowTlsHostnameMismatch);
```
Do the equivalent at the `ModelApiConnection` call site (pass its `config.AllowTlsHostnameMismatch`).

- [ ] **Step 6: Run tests + lint**

Run: `make test` then `make lint`. Expected: PASS / exit 0.

- [ ] **Step 7: Commit**

```bash
git commit -am "fix(tls): make hostname-mismatch rejection opt-out via AllowTlsHostnameMismatch (A3)"
```

---

## Task 2: B7 — Correct Windows argument quoting

**Files:**
- Modify: `FluentDocker/Drivers/Docker/Cli/DockerCliDriverBase.cs:202-230` (`QuoteArgumentIfNeeded`)
- Test: `FluentDocker.Tests/CoreTests/.../DockerCliDriverBaseTests.cs` (create if absent)

**Why:** `QuoteArgumentIfNeeded` does `argument.Replace("\\", "\\\\")` unconditionally, corrupting Windows paths like `C:\Program Files\m.gguf` (used by `package --gguf/--license`). The correct rule (Windows `CommandLineToArgvW`): backslashes are only doubled when they immediately precede the closing quote or an embedded quote.

> **First choice if feasible:** switch the package-command execution path to `ProcessStartInfo.ArgumentList` (no manual quoting at all). Read `DockerCliDriverBase.Execution.cs` to see if the command is assembled as a single string; if it is string-based and costly to change, apply the corrected algorithm below.

**Interfaces:**
- Produces: `QuoteArgumentIfNeeded` returns a string that `CommandLineToArgvW` round-trips back to the original argument on Windows.

- [ ] **Step 1: Write failing tests (cross-platform — pure string logic)**

```csharp
[Theory]
[Trait("Category", "Unit")]
[InlineData(@"C:\Program Files\m.gguf", "\"C:\\Program Files\\m.gguf\"")]
[InlineData(@"C:\tmp\no-space", @"C:\tmp\no-space")]            // no quoting needed
[InlineData(@"a\\b c", "\"a\\\\b c\"")]                          // interior backslashes preserved
[InlineData(@"ends\", "\"ends\\\\\"")]                            // trailing backslash doubled before closing quote
public void QuotesWindowsPathsWithoutCorruption(string input, string expected)
    => Assert.Equal(expected, DockerCliDriverBaseTestHook.Quote(input));
```
(Expose a test hook or make `QuoteArgumentIfNeeded` `internal` + `InternalsVisibleTo` already present.)

- [ ] **Step 2: Run — expect FAIL** (current code double-escapes interior backslashes).

- [ ] **Step 3: Implement the correct algorithm**

```csharp
protected static string QuoteArgumentIfNeeded(string argument)
{
  if (string.IsNullOrEmpty(argument))
    return "\"\"";

  var needsQuoting = argument.IndexOfAny([' ', '\t', '"']) >= 0;
  if (!needsQuoting)
    return argument;

  var sb = new System.Text.StringBuilder();
  sb.Append('"');
  for (var i = 0; i < argument.Length; i++)
  {
    var backslashes = 0;
    while (i < argument.Length && argument[i] == '\\') { backslashes++; i++; }

    if (i == argument.Length)
    {
      sb.Append('\\', backslashes * 2);   // before closing quote: double
      break;
    }
    if (argument[i] == '"')
    {
      sb.Append('\\', backslashes * 2 + 1); // before a quote: double + escape the quote
      sb.Append('"');
    }
    else
    {
      sb.Append('\\', backslashes);          // interior: leave as-is
      sb.Append(argument[i]);
    }
  }
  sb.Append('"');
  return sb.ToString();
}
```

- [ ] **Step 4: Run tests + lint.** Expected PASS.

- [ ] **Step 5: Commit**

```bash
git commit -am "fix(cli): correct Windows backslash quoting so paths with spaces are not corrupted (B7)"
```

---

## Task 3: B8 — Kill the child process on the oversized-output / exception path

**Files:**
- Modify: `FluentDocker/Drivers/Docker/Cli/DockerCliDriverBase.Execution.cs:150-181`
- Test: `DockerCliDriverBaseTests.cs`

**Why:** Verified — when `ReadBoundedAsync` throws (e.g. output exceeds the 4 MiB cap), the `catch (Exception)` returns an error result without `process.Kill()`; only the `finally` disposes (closes pipes) — the child may keep running.

- [ ] **Step 1: Add a kill in the exception path** (inside the existing `catch (Exception ex) when (...)` before returning the error result):

```csharp
try { if (process is { HasExited: false }) process.Kill(entireProcessTree: true); }
catch { /* best effort — process may have exited between the check and the kill */ }
```

- [ ] **Step 2: Test** — simulate an oversized stdout (a fake/echo command that emits > cap) and assert the returned result is a failure AND the process is no longer running. If a real process is awkward in a unit test, mark this `[Trait("Category","Integration")]` instead and assert behavior via a long-output container.

- [ ] **Step 3: Run tests + lint, commit**

```bash
git commit -am "fix(cli): kill docker child process on oversized-output/exception path (B8)"
```

---

## Task 4: B10 — Last `WithInferenceDriver` call wins (both builders)

**Files:**
- Modify: `FluentDocker/Builders/ModelRunnerBuilder.cs:87-97`
- Modify: `FluentDocker/Builders/ModelServiceBuilder.cs:78-88`
- Test: `FluentDocker.Tests/CoreTests/BuilderTests/` (model builder tests)

**Why:** Verified — each builder has `_inferenceDriver` and `_inferenceDriverId`; neither overload clears the other, and resolution checks the instance first, so `WithInferenceDriver(instance).WithInferenceDriver(id)` silently ignores the id.

- [ ] **Step 1: Failing test**

```csharp
[Fact]
[Trait("Category", "Unit")]
public void WithInferenceDriver_LastCallWins_InstanceThenId()
{
    var b = new ModelRunnerBuilder(kernel, driverId);
    b.WithInferenceDriver(Mock.Of<IModelInferenceDriver>());
    b.WithInferenceDriver("some-driver-id");
    // Build/resolve and assert the id-resolved driver is used, not the stale instance.
}
```

- [ ] **Step 2: Each overload clears the alternate field** (both files):

```csharp
public IModelRunnerBuilder WithInferenceDriver(IModelInferenceDriver inference)
{
  _inferenceDriver = inference ?? throw new ArgumentNullException(nameof(inference));
  _inferenceDriverId = null;           // last call wins
  return this;
}

public IModelRunnerBuilder WithInferenceDriver(string driverId)
{
  _inferenceDriverId = driverId ?? throw new ArgumentNullException(nameof(driverId));
  _inferenceDriver = null;             // last call wins
  return this;
}
```
Apply the analogous two-line clears in `ModelServiceBuilder.cs`.

- [ ] **Step 3: Run tests + lint, commit**

```bash
git commit -am "fix(builders): WithInferenceDriver last-call-wins (clear alternate field) (B10)"
```

---

## Task 5: C11 — Non-streaming inference returns failed CommandResponse, never throws (except cancellation)

**Files:**
- Modify: `FluentDocker/Drivers/Docker/Api/Components/DockerApiModelInferenceDriver.cs:89-127` (`PostJsonAsync`)
- Test: `DockerApiModelInferenceDriverTests.cs`

**Why:** Verified (narrowed) — HTTP errors already return `Fail`, but a **null/unparseable body throws `ModelRunnerException`** (lines 111 & 122) out of a method typed `Task<CommandResponse<T>>`. Only `OperationCanceledException` should stay exceptional.

- [ ] **Step 1: Failing test** — feed a connection that returns a 200 with an empty/`null` body; assert the call returns `Success == false` with `ErrorCodes.ModelInference.StreamParseError`, and does **not** throw.

- [ ] **Step 2: Convert the null-body throw to a failed response** and drop the `catch (ModelRunnerException) { throw; }` re-throw for this non-streaming path:

```csharp
var dto = await JsonSerializer.DeserializeAsync<TResponse>(...).ConfigureAwait(false);
if (dto is null)
  return CommandResponse<TResponse>.Fail(
      $"{operation}: inference response body was null (expected JSON object)",
      ErrorCodes.ModelInference.StreamParseError,
      CreateApiErrorContext(context, operation, response));
return CommandResponse<TResponse>.Ok(dto);
```
Keep the outer `catch (Exception ex) when (ex is not OperationCanceledException) { return Fail(ex.Message, ...); }` so any other model error also becomes a failed response; cancellation still propagates.

- [ ] **Step 3: Run tests + lint, commit**

```bash
git commit -am "fix(inference): non-streaming returns failed CommandResponse on null body instead of throwing (C11)"
```

---

## Task 6: C12 — Add a configurable per-read idle timeout for streaming

**Files:**
- Modify: `FluentDocker/Drivers/Models/Connection/ModelApiConnectionConfig.cs` (add `StreamReadIdleTimeout`)
- Modify: `FluentDocker/Drivers/Models/Connection/ModelApiConnection.cs` (the SSE read loop consuming the stream from `PostStreamAsync`)
- Test: `ModelApiConnectionTests.cs` (streaming/cancellation tests already exist)

**Why:** Verified — `HttpClient.Timeout = Timeout.InfiniteTimeSpan` and streaming reads rely solely on the caller's token, so a server that wedges mid-stream hangs forever. This is hardening, not a crash.

- [ ] **Step 1: Add the config knob** (nullable; null = disabled, preserving today's behavior):

```csharp
/// <summary>Max time to wait for the next streamed chunk before aborting the read.
/// Null disables the idle timeout (wait indefinitely, honoring only cancellation).</summary>
public TimeSpan? StreamReadIdleTimeout { get; set; }
```

- [ ] **Step 2: Locate the per-frame read loop** (where the stream returned by `PostStreamAsync` is read into SSE frames). Wrap each read with a linked CTS that cancels after the idle timeout:

```csharp
async Task<int> ReadWithIdleTimeoutAsync(Stream s, byte[] buf, CancellationToken ct)
{
  if (_streamReadIdleTimeout is not { } idle)
    return await s.ReadAsync(buf, ct).ConfigureAwait(false);
  using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
  idleCts.CancelAfter(idle);
  try { return await s.ReadAsync(buf, idleCts.Token).ConfigureAwait(false); }
  catch (OperationCanceledException) when (!ct.IsCancellationRequested)
  { throw new ModelRunnerException("Streaming read idle-timed out", ErrorCodes.ModelInference.EndpointUnreachable); }
}
```

- [ ] **Step 3: Test** — a stream that stalls (a `Stream` whose `ReadAsync` never completes) with a 100 ms idle timeout throws `ModelRunnerException`; with idle timeout null it keeps waiting until the caller cancels.

- [ ] **Step 4: Run tests + lint, commit**

```bash
git commit -am "feat(inference): configurable per-read idle timeout for SSE streaming (C12)"
```

---

## Task 7: C14 — Parse preview-CLI tables by header-column offsets

**Files:**
- Modify: `FluentDocker/Drivers/Docker/Cli/Components/Parsing/ModelJsonParser.cs:98-141, 346-367`
- Test: `FluentDocker.Tests/CoreTests/.../ModelJsonParserTests.cs` using fixtures `FluentDocker.Tests/Fixtures/Dmr/ls.txt`, `ps.txt`

**Why:** Verified — `ParseLsTable`/`ParsePsTable` index fixed positions after a 2+-space split (`fields[1]`, `fields[2]`…); a missing column (the fixture's empty `CONTEXT`) or a name with spaces shifts every index. Caveat: `ps`/`df` have **no `--json`** in the current DMR (verified), so JSON is not an option there — header-offset parsing is the real fix. `ls`/`inspect` already prefer JSON elsewhere.

- [ ] **Step 1: Failing test** — feed a fixture with a blank middle column and assert the architecture/size land in the right fields (current code mis-assigns).

- [ ] **Step 2: Replace whitespace-index parsing with header-offset parsing.** Compute each column's start index from the header row (the run of `HEADER` tokens and the gaps), then slice each data line by `[colStart..nextColStart]` and trim. Reject rows whose first cell isn't a valid `ModelReference`. Keep the existing regex helpers (`HexId`, `SizeRegex`) as a secondary locator for `Id`/`Size`.

Sketch:
```csharp
static (string name, int start)[] ParseHeader(string headerLine, params string[] cols) { /* find each col's index */ }
static string Cell(string row, int start, int end) =>
    start >= row.Length ? "" : row[start..Math.Min(end, row.Length)].Trim();
```

- [ ] **Step 3: Test both `ls` and `ps` fixtures, plus a synthetic blank-column row.** Run: `dotnet test --filter "FullyQualifiedName~ModelJsonParserTests" --framework net10.0`.

- [ ] **Step 4: Run `make test` + lint, commit**

```bash
git commit -am "fix(parse): parse DMR ls/ps tables by header-column offsets, reject ambiguous rows (C14)"
```

---

## Task 8: D19 — Reject NaN/Infinity in `LlamaCppRuntimeFlags`

**Files:**
- Modify: `FluentDocker/Model/Models/Options/LlamaCppRuntimeFlags.cs:116-127` (`AddDouble`)
- Test: `LlamaCppRuntimeFlagsTests.cs`

**Why:** Verified — range checks use `<`/`>`, which are false for NaN, so NaN/Infinity pass through into CLI args.

- [ ] **Step 1: Failing test** — `new LlamaCppRuntimeFlags { Temperature = double.NaN }.ToArgs()` should throw `ArgumentOutOfRangeException` (currently emits `NaN`).

- [ ] **Step 2: Add a finiteness guard** at the top of `AddDouble` (after `if (!value.HasValue) return;`):

```csharp
var v = value.Value;
if (!double.IsFinite(v))
  throw new ArgumentOutOfRangeException(name, v, $"{flag} must be a finite number.");
```

- [ ] **Step 3: Run tests + lint, commit**

```bash
git commit -am "fix(model): reject NaN/Infinity in LlamaCppRuntimeFlags double options (D19)"
```

---

## Task 9: D20 — Distinct `UninstallFailed` error code

**Files:**
- Modify: `FluentDocker/Model/Drivers/ErrorCodes.cs` (add `UninstallFailed` near `InstallFailed = "MDL_021"`)
- Modify: `FluentDocker/Drivers/Docker/Cli/Components/DockerCliModelRuntimeDriver.cs:379`
- Test: the runtime-driver unit tests

**Why:** Verified — `UninstallRunnerAsync` returns `ErrorCodes.Model.InstallFailed`; no `UninstallFailed` code exists, so callers can't distinguish.

- [ ] **Step 1: Add the code** (use the next free `MDL_0xx`; confirm by scanning `ErrorCodes.cs`):

```csharp
public const string UninstallFailed = "MDL_026"; // adjust to the next unused number
```

- [ ] **Step 2: Use it at line 379:**

```csharp
return await SimpleUnitAsync(context, sb.ToString(), "UninstallRunner", ErrorCodes.Model.UninstallFailed, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 3: Failing-then-passing test** — assert an uninstall failure surfaces `MDL_026`, not `MDL_021`.

- [ ] **Step 4: Run tests + lint, commit**

```bash
git commit -am "fix(model): report uninstall failures with distinct UninstallFailed code (D20)"
```

---

## Self-review checklist

- [ ] A3 default behavior unchanged (strict) — only opt-in relaxes it; both call sites pass the flag.
- [ ] B7 round-trips through `CommandLineToArgvW` semantics; interior backslashes preserved.
- [ ] B8 kill is best-effort and guarded by `HasExited`.
- [ ] B10 fixed in **both** `ModelRunnerBuilder` and `ModelServiceBuilder`.
- [ ] C11 only `OperationCanceledException` stays exceptional for the non-streaming path.
- [ ] C12 idle timeout is opt-in (null = today's behavior).
- [ ] C14 does NOT claim JSON for `ps`/`df` (no `--json` there).
- [ ] B9 (refuted) left untouched.
- [ ] `make test` + `make lint` green after each task.
