# Refactor B — Delete `IFeature` / `FeatureAttribute` dead code

Plan date: 2026-05-11
Scope: delete the v2 plugin/feature abstraction (`IFeature`, `FeatureAttribute`,
`FeatureConstants`) and its test class. Zero internal references — pure
dead code removal.

## Why

`IFeature` was the v2 plugin discovery mechanism. The v3 architecture replaced
it with the driver-pack model (`IDriverPack`, `IDriverInterfaceResolver`,
`IDriverScopedBuilder`). The old types are kept for back-compat only — but
grep confirms they have **zero callers** outside their own definition files
and the test class that exercises them.

The `[Obsolete("v2 legacy type. Will be removed in v4.")]` annotations already
say what the intent is; we're just bringing the v4 removal forward to v3.

## Outcome

- `FluentDocker/Model/IFeature.cs` deleted (contains `IFeature` interface +
  `FeatureConstants` static class).
- `FluentDocker/Common/FeatureAttribute.cs` deleted.
- `FluentDocker.Tests/CoreTests/Common/FeatureAttributeTests.cs` deleted (it
  is the only file in the entire codebase that uses these types).
- Net diff: 3 file deletions, no other code changes.

## Files affected

### Source (FluentDocker)

| File | Change kind |
|---|---|
| `FluentDocker/Model/IFeature.cs` | **DELETE** |
| `FluentDocker/Common/FeatureAttribute.cs` | **DELETE** |

### Tests (FluentDocker.Tests)

| File | Change kind |
|---|---|
| `FluentDocker.Tests/CoreTests/Common/FeatureAttributeTests.cs` | **DELETE** entire file (it only tests the deleted code, no other coverage is lost) |

### Docs

| File | Change kind |
|---|---|
| `docs/` | None — confirmed via `grep -rn "IFeature\|FeatureAttribute\|FeatureConstants" docs/` returning zero hits during audit. Verify before completing. |

## Tasks

### Step 1 — Pre-flight grep (confirm dead code)

Before deleting anything, run:
```sh
grep -rn "\bIFeature\b\|\bFeatureAttribute\b\|\bFeatureConstants\b" \
  FluentDocker/ FluentDocker.Testing.*/ FluentDocker.Tests*/ docs/ 2>/dev/null \
  | grep -v "/bin/\|/obj/" \
  | grep -v "^FluentDocker/Model/IFeature\.cs:" \
  | grep -v "^FluentDocker/Common/FeatureAttribute\.cs:" \
  | grep -v "^FluentDocker\.Tests/CoreTests/Common/FeatureAttributeTests\.cs:"
```

Expected output: **empty**. If any line appears, do NOT proceed — investigate
that reference first. The audit on 2026-05-11 confirmed empty.

### Step 2 — Delete the three files

```sh
rm FluentDocker/Model/IFeature.cs
rm FluentDocker/Common/FeatureAttribute.cs
rm FluentDocker.Tests/CoreTests/Common/FeatureAttributeTests.cs
```

(Use the `rm` Bash command. There's nothing to edit — these files have no
inbound references.)

### Step 3 — Check for orphaned `using FluentDocker.Common;` or `using FluentDocker.Model;` that only existed for these types

Optional cleanup — extremely unlikely since both namespaces have many other
types. If you spot any file that newly emits `CS0246` (using directive points
to a missing type) after the build, fix it. The build will tell you.

### Step 4 — Confirm the `csproj` doesn't list the deleted files explicitly

```sh
grep -rln "IFeature\|FeatureAttribute" /Users/mariotoffia/progs/github/FluentDocker --include="*.csproj" 2>/dev/null
```

Expected: empty. The project uses wildcard includes so deletion is enough.

## Verification

After Step 2:
```sh
grep -rn "\bIFeature\b\|\bFeatureAttribute\b\|\bFeatureConstants\b" \
  FluentDocker/ FluentDocker.Testing.*/ FluentDocker.Tests*/ docs/ 2>/dev/null \
  | grep -v "/bin/\|/obj/"
```
Expected: zero hits.

Final:
```sh
make build
make test
```
Both must succeed. The test count should drop by exactly the number of tests
in `FeatureAttributeTests.cs` (the audit run showed 8 `[Fact]` tests there);
no other test should fail.

## Rollback

Single `git restore` brings everything back. Safest of the four refactors —
no editing, just deletion.

## Out of scope

- `IService` removal — REFACTOR_A1.
- `DriverComponent` removal — REFACTOR_A2.
- `Services.NetworkCreateConfig` removal — REFACTOR_A3.
