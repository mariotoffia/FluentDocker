# Phase 8: Build Warning Cleanup

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate all 1537 build warnings across the FluentDocker solution to achieve a clean build.

**Architecture:** Mechanical fixes grouped by warning category. Each task targets one CA/CS rule across all affected files. No behavioral changes — only code quality improvements that preserve existing semantics.

**Tech Stack:** C# .NET 10, Roslyn analyzers, xUnit v3

---

## Summary

| Task | Warning | Count (unique) | Files | Risk |
|------|---------|----------------|-------|------|
| 1 | CA1507 | 85 | 3 | None |
| 2 | CA1852 | 42 | 16 | None |
| 3 | CA1861 | 37 | 21 | Low |
| 4 | CA1866 | 28 | 17 | None |
| 5 | CA1822 | 36 | 11 | None |
| 6 | CA1816 | 22 | 12 | None |
| 7 | CA1510 | 20 | 6 | None |
| 8 | CA1859 | 13 | ~8 | Low |
| 9 | Minor CAs | ~40 | ~20 | Low |
| 10 | CS1570/1572 | 10 | 3 | None |
| 11 | CS8604 (tests) | ~138 | ~105 | None |
| 12 | CS8604 (testing pkgs) | ~138 | ~15 | None |
| 13 | CS0067 | 1 | 1 | None |

**Verification command:** `dotnet clean FluentDocker.sln -v q && dotnet build FluentDocker.sln -c Debug 2>&1 | grep "Warning(s)"`
Target: `0 Warning(s)`

---

## Chunk 1: Main Library Code Analysis Warnings

### Task 1: CA1507 — Use `nameof` instead of string literals

**Files:**
- Modify: `FluentDocker/Drivers/ComposeModels.cs`
- Modify: `FluentDocker/Drivers/Docker/Api/ApiModels/DockerApiContainerModels.cs`
- Modify: `FluentDocker/Drivers/Docker/Cli/Components/DockerSystemModels.cs`

**What:** CA1507 fires when a string literal matches a parameter/property name. Replace `"Name"` with `nameof(Name)`.

**Caution:** Only apply when the string truly references the same symbol. JSON `[JsonPropertyName("name")]` attributes are NOT candidates — those are serialization keys, not symbol references. CA1507 only fires on `ArgumentException`-style usages and LINQ expressions.

- [ ] **Step 1:** For each file, find all CA1507 warnings and replace string literals with `nameof(...)`. Example:
  ```csharp
  // Before
  throw new ArgumentNullException("context");
  // After
  throw new ArgumentNullException(nameof(context));
  ```

- [ ] **Step 2:** Build and verify CA1507 count is 0:
  ```bash
  dotnet build FluentDocker/FluentDocker.csproj -c Debug 2>&1 | grep "CA1507" | wc -l
  ```
  Expected: 0

- [ ] **Step 3:** Run `make test` — all 1807 tests pass.

---

### Task 2: CA1852 — Seal internal types

**Files:** 16 files across `Builders/`, `Drivers/`, `Model/`, `Services/`, `Testing/`

**What:** Internal classes with no subtypes should be `sealed` for performance (JIT devirtualization).

**Caution:** Only seal types that are truly `internal` (no `public` modifier). Don't seal types that are `public` — CA1852 only fires on non-externally-visible types.

- [ ] **Step 1:** For each CA1852 warning, add `sealed` keyword:
  ```csharp
  // Before
  internal class BuildOperation { ... }
  // After
  internal sealed class BuildOperation { ... }
  ```

- [ ] **Step 2:** Build and verify CA1852 count is 0:
  ```bash
  dotnet build FluentDocker/FluentDocker.csproj -c Debug 2>&1 | grep "CA1852" | wc -l
  ```

- [ ] **Step 3:** Run `make test` — all tests pass.

---

### Task 3: CA1861 — Extract constant arrays to `static readonly` fields

**Files:** 21 files across `Executors/Parsers/`, `Builders/`, `Common/`, `Drivers/`, `Services/`

**What:** Array arguments passed repeatedly to methods (e.g., `string.Split(new[] { '\n', '\r' })`) should be `static readonly` fields to avoid repeated heap allocation.

**Pattern:**
```csharp
// Before (in method body)
var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

// After (static field + method usage)
private static readonly char[] LineSeparators = ['\n', '\r'];
// ...
var lines = output.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
```

**Caution:** Use collection expression syntax `['\n', '\r']` on .NET 10. Name the field descriptively (e.g., `LineSeparators`, `CommaSeparator`). If multiple methods in the same class use the same array, share one field.

- [ ] **Step 1:** Process each file. Extract arrays to `private static readonly` fields at class level.

- [ ] **Step 2:** Build and verify CA1861 count is 0.

- [ ] **Step 3:** Run `make test` — all tests pass.

---

### Task 4: CA1866 — Use `char` overload of string methods

**Files:** 17 files across `Drivers/`, `Executors/`, `Services/`, `Builders/`

**What:** `string.StartsWith("x")` / `EndsWith("x")` / `Contains("x")` / `IndexOf("x")` with single-character strings should use the `char` overload for performance.

```csharp
// Before
if (line.StartsWith("/")) ...
// After
if (line.StartsWith('/')) ...
```

**Caution:** Don't change multi-character strings. Don't change calls that use `StringComparison` overloads where the char overload doesn't accept them.

- [ ] **Step 1:** Replace single-character string overloads with char overloads in all 17 files.

- [ ] **Step 2:** Build and verify CA1866 count is 0.

- [ ] **Step 3:** Run `make test` — all tests pass.

---

### Task 5: CA1822 — Mark members as `static`

**Files:** 11 files in main library

**What:** Methods that don't access instance data should be `static`.

**Caution:** Don't mark `virtual`/`override`/`abstract` methods static. Don't mark methods that are part of an interface implementation. Check that making a method static doesn't break callers that call via instance reference (shouldn't in internal code).

- [ ] **Step 1:** Add `static` keyword to each flagged method.

- [ ] **Step 2:** Build and verify CA1822 count is 0 for main library.

- [ ] **Step 3:** Run `make test`.

---

### Task 6: CA1816 — Add `GC.SuppressFinalize` to Dispose/DisposeAsync

**Files:** 12 files across `Services/Impl/`, `Kernel/`, `Model/`, `Testing/`

**What:** `Dispose()` and `DisposeAsync()` implementations should call `GC.SuppressFinalize(this)` to prevent finalization of already-disposed objects.

```csharp
// In Dispose()
public void Dispose()
{
    // existing disposal logic...
    GC.SuppressFinalize(this);
}

// In DisposeAsync()
public async ValueTask DisposeAsync()
{
    // existing disposal logic...
    GC.SuppressFinalize(this);
}
```

**Caution:** Add `GC.SuppressFinalize(this)` at the END of the method (after all disposal work). For `DisposeAsync`, place it after the last `await`.

- [ ] **Step 1:** Add `GC.SuppressFinalize(this)` to each flagged Dispose/DisposeAsync method.

- [ ] **Step 2:** Build and verify CA1816 count is 0.

- [ ] **Step 3:** Run `make test`.

---

### Task 7: CA1510 — Use `ArgumentNullException.ThrowIfNull`

**Files:** 6 files — `Common/ErrorContextExtensions.cs`, `Kernel/`, `Builders/`, `Services/`

**What:** Replace manual null-check-then-throw with the built-in helper.

```csharp
// Before
if (context == null) throw new ArgumentNullException(nameof(context));
// After
ArgumentNullException.ThrowIfNull(context);
```

**Caution:** `ThrowIfNull` uses `CallerArgumentExpression` to auto-capture the param name, so `nameof()` is not needed. Only apply to `ArgumentNullException` — not to other exception types with null checks.

- [ ] **Step 1:** Replace patterns in all 6 files.

- [ ] **Step 2:** Build and verify CA1510 count is 0.

- [ ] **Step 3:** Run `make test`.

---

### Task 8: CA1859 — Use concrete types for improved performance

**Files:** ~8 files

**What:** Local variables/returns typed as interfaces (e.g., `IList<T>`) where the concrete type (e.g., `List<T>`) never leaves the method should use the concrete type.

```csharp
// Before
IList<string> result = new List<string>();
// After
List<string> result = new List<string>();
```

**Caution:** Don't change public API return types. Only change private/local variables where the concrete type is used within the same scope. If the variable is returned from a method, only change if the method's return type can also change (private methods).

- [ ] **Step 1:** Fix each CA1859 warning. Change interface types to concrete types where safe.

- [ ] **Step 2:** Build and verify CA1859 count is 0.

- [ ] **Step 3:** Run `make test`.

---

### Task 9: Minor CA warnings (CA1806, CA1710, CA1309, CA1000, CA1805, CA1716, CA2249, CA1847)

**What:** Grouped minor warnings (~40 unique instances across ~20 files):
- **CA1806** (8): Don't ignore method return values — assign or remove call
- **CA1710** (8): Collection type names should have correct suffix (e.g., `Dictionary` suffix)
- **CA1309** (8): Use `StringComparison.Ordinal` instead of culture-sensitive comparison
- **CA1000** (8): Don't declare static members on generic types
- **CA1805** (6): Don't initialize to default values
- **CA1716** (6): Identifiers shouldn't match VB/C# keywords
- **CA2249** (4): Use `string.Contains(char)` instead of `IndexOf`
- **CA1847** (~9): Use `char` for single-char `string.Contains/Replace`

**Strategy:** Some of these (CA1710, CA1000, CA1716) may be intentional API design choices. For those, suppress with `[SuppressMessage]` or add to `.editorconfig`. For mechanical fixes (CA1806, CA1309, CA1805, CA2249, CA1847), apply the fix.

- [ ] **Step 1:** Fix mechanical warnings (CA1806, CA1309, CA1805, CA2249, CA1847).

- [ ] **Step 2:** For design-choice warnings (CA1710, CA1000, CA1716), add targeted suppressions in `.editorconfig` or `NoWarn` for specific files, OR apply the fix if the rename is acceptable.

- [ ] **Step 3:** Build and verify all minor CA warnings are resolved.

- [ ] **Step 4:** Run `make test`.

---

### Task 10: CS1570/CS1572/CS1574/CS1587 — XML doc comment fixes

**Files:** ~3 files

**What:**
- **CS1572** (5): XML comment `<param>` tag for parameter that doesn't exist
- **CS1570** (4): Badly formed XML in doc comment
- **CS1574** (1): XML comment cref attribute couldn't be resolved
- **CS1587** (1): XML comment not placed on valid language element

- [ ] **Step 1:** Fix each XML doc warning — remove stale `<param>` tags, fix malformed XML, correct cref references.

- [ ] **Step 2:** Build and verify CS15xx count is 0.

- [ ] **Step 3:** Run `make test`.

---

## Chunk 2: Test Project & Testing Package Warnings

### Task 11: CS8600/CS8604/CS8625/CS8601/CS8602/CS8603 — Nullable warnings in test project

**Files:** ~105 test files

**What:** The test project has `<Nullable>enable</Nullable>` but many tests intentionally pass `null` or work with possibly-null values without null-forgiving operators.

**Strategy:** Use `!` (null-forgiving operator) where tests intentionally work with nulls. Use `?` for variables that are legitimately nullable. Don't suppress the entire warning — fix each occurrence.

Common patterns:
```csharp
// CS8600: Converting null to non-nullable
// Before
string id = result.Data.Id;
// After (when Data might be null in general but test asserts it's not)
string id = result.Data!.Id;

// CS8604: Possible null argument
// Before
await ApiRemoveContainerAsync(containerId);
// After
await ApiRemoveContainerAsync(containerId!);

// CS8625: Cannot convert null to non-nullable
// Before
var result = SomeMethod(null);
// After
var result = SomeMethod(null!);
```

**Caution:** Only add `!` when the test is intentionally testing null behavior or when a prior assertion guarantees non-null. Don't mask real bugs — if a test genuinely has a null issue, fix the test logic.

- [ ] **Step 1:** Process test files in batches (~20 files at a time). Add `!` operators or `?` nullable annotations as appropriate.

- [ ] **Step 2:** Build and verify CS86xx count is 0 for test project:
  ```bash
  dotnet build FluentDocker.Tests/FluentDocker.Tests.csproj -c Debug 2>&1 | grep "warning CS86" | wc -l
  ```

- [ ] **Step 3:** Run `make test` — all 1807 tests pass.

---

### Task 12: CS8604 + CA1816 + CA1510 — Testing package warnings

**Files:** ~15 files across `FluentDocker.Testing.Xunit/`, `FluentDocker.Testing.NUnit/`, `FluentDocker.Testing.MsTest/`

**What:**
- **CS8604** (138): Nullable warnings — apply `!` or `?` as in Task 11
- **CA1816** (26): Add `GC.SuppressFinalize` as in Task 6
- **CA1510** (4): Use `ThrowIfNull` as in Task 7
- **IDE0021** (4): Use expression body for property accessor

- [ ] **Step 1:** Fix all warnings in testing packages.

- [ ] **Step 2:** Build and verify 0 warnings for all three testing packages.

- [ ] **Step 3:** Run `make test`.

---

### Task 13: CS0067 — Event never used

**Files:** 1 file

**What:** An event is declared but never raised. Either raise it or remove it if dead code.

- [ ] **Step 1:** Find the event, determine if it should be raised somewhere or removed.

- [ ] **Step 2:** Build and verify CS0067 is gone.

- [ ] **Step 3:** Run `make test`.

---

## Chunk 3: Final Verification

### Task 14: Clean build verification

- [ ] **Step 1:** Clean build entire solution:
  ```bash
  dotnet clean FluentDocker.sln -v q && dotnet build FluentDocker.sln -c Debug 2>&1 | grep "Warning(s)"
  ```
  Expected: `0 Warning(s)`

- [ ] **Step 2:** Run full test suite:
  ```bash
  make test
  ```
  Expected: 1807+ tests, 0 failures

- [ ] **Step 3:** Run pack to verify NuGet packages build cleanly:
  ```bash
  make pack 2>&1 | grep -E "Warning|Error"
  ```
  Expected: 0 warnings, 0 errors
