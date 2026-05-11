# Refactor A2 — Remove `DriverComponent` enum

Plan date: 2026-05-11
Scope: complete the v3 deprecation by deleting the `[Obsolete]`
`DriverComponent` enum and the `ISysCtl.SysCtl(string, DriverComponent)`
overload that depends on it.

## Why

`DriverComponent` (see `FluentDocker/Model/Drivers/DriverComponent.cs:8`) is
already marked obsolete and slated for v4 removal. The replacement —
generic `SysCtl<T>(driverId)` / type-based `SysCtl(driverId, Type)` — is fully
implemented. The enum still exists because three driver packs implement a
switch on it to satisfy `ISysCtl.SysCtl(string, DriverComponent)`. Removing
that interface method unblocks deleting the enum and 30+ lines of switch boilerplate
across the codebase.

## Outcome

- `ISysCtl.SysCtl(string, DriverComponent)` overload removed.
- `DriverComponent` enum file deleted.
- `FluentDockerKernel.SysCtl(string, DriverComponent)` and the private
  `ComponentToType` helper deleted.
- 3 driver pack switch implementations deleted.
- Test mocks and obsolete-overload-targeted tests cleaned up.
- All `#pragma warning disable CS0618 // DriverComponent obsolete` removed.

## Files affected

### Source (FluentDocker)

| File | Change kind |
|---|---|
| `FluentDocker/Model/Drivers/DriverComponent.cs` | **DELETE** entire file |
| `FluentDocker/Kernel/ISysCtl.cs` | Remove obsolete method declaration (line 12-19) |
| `FluentDocker/Kernel/FluentDockerKernel.cs` | Remove `SysCtl(string, DriverComponent)` impl + `ComponentToType` helper + pragma (line 9) |
| `FluentDocker/Drivers/Docker/Cli/DockerCliDriverPack.cs` | Remove `SysCtl(string, DriverComponent)` switch (~line 162-170) + obsolete-using `using` if scoped |
| `FluentDocker/Drivers/Docker/Api/DockerApiDriverPack.cs` | Same (~line 152-160) |
| `FluentDocker/Drivers/Podman/Cli/PodmanCliDriverPack.cs` | Same (~line 171-180) |

### Tests (FluentDocker.Tests)

| File | Change kind |
|---|---|
| `FluentDocker.Tests/Mocks/MockDriverPack.cs` | Remove `SysCtl(string, DriverComponent)` impl (line 141) + `using DriverComponent` alias (line 13) + pragma (line 20) |
| `FluentDocker.Tests/CoreTests/Driver/Podman/PodmanCliDriverPackTests.cs` | Remove 6 tests that call `pack.SysCtl("podman", DriverComponent.X)` (lines 71, 117, 181, 196, 219, 242) + pragma (line 10) |
| `FluentDocker.Tests/CoreTests/Driver/Docker/DockerCliDriverPackTests.cs` | Remove the `[Theory] [InlineData(DriverComponent.X)]` test (line 372 onwards) + the call-site at line 95 + pragma (line 16) |

### Docs

| File | Change kind |
|---|---|
| `docs/architecture.md` | Remove the `DriverComponent` section (~line 178) and the `[Obsolete]` note added by DOCS_SYNC_PLAN P2.7 — both go away |
| `docs/extensibility.md` | Same (~line 94 + the note added by P2.7) |

## Tasks

### Step 1 — Remove the obsolete method declaration from `ISysCtl`

In `FluentDocker/Kernel/ISysCtl.cs`, delete lines 12–19:
```csharp
/// <summary>
/// Gets a driver component interface by driver ID and component enum.
/// </summary>
/// ...
[Obsolete("Use SysCtl<T>(driverId) or SysCtl(driverId, Type) instead. Will be removed in v4.")]
object SysCtl(string driverId, DriverComponent component);
```

Also remove `using FluentDocker.Model.Drivers;` if it becomes unused after
this edit (check with `grep "DriverComponent\|DriverType\|RuntimeType" ISysCtl.cs`).

### Step 2 — Update `FluentDockerKernel.cs`

Delete:
- Line 9: `#pragma warning disable CS0618 // DriverComponent obsolete — intentional usage` and the matching `restore` if present.
- Lines 119–124 (approx): the public `SysCtl(string driverId, DriverComponent component)` method.
- Lines 126–138 (approx): the private `ComponentToType` helper.

Verify after edit:
```sh
grep -n "DriverComponent" FluentDocker/Kernel/FluentDockerKernel.cs
```
Expected: zero hits.

### Step 3 — Strip switch impls from the 3 driver packs

For each of:
- `FluentDocker/Drivers/Docker/Cli/DockerCliDriverPack.cs`
- `FluentDocker/Drivers/Docker/Api/DockerApiDriverPack.cs`
- `FluentDocker/Drivers/Podman/Cli/PodmanCliDriverPack.cs`

Find the method block that implements `SysCtl(string driverId, DriverComponent component)`
(approximate lines: 162 / 152 / 171). It looks roughly like:
```csharp
[Obsolete(...)]
public object SysCtl(string driverId, DriverComponent component) => component switch
{
  DriverComponent.Container => _containerDriver,
  DriverComponent.Image => _imageDriver,
  // ...
  _ => throw new ...
};
```

Delete the entire method. Also remove any `#pragma warning disable CS0618`
directive that was protecting it (check the top of each file). Remove
`using FluentDocker.Model.Drivers;` if unused (`DriverType` and `RuntimeType`
live there too, so verify first).

### Step 4 — Delete the enum file

`rm FluentDocker/Model/Drivers/DriverComponent.cs` (use the `rm` Bash command,
not in-editor — file deletion is the goal).

### Step 5 — Clean up `MockDriverPack.cs`

In `FluentDocker.Tests/Mocks/MockDriverPack.cs`:
- Delete line 13: `using DriverComponent = FluentDocker.Model.Drivers.DriverComponent;`
- Delete line 20: `#pragma warning disable CS0618 // DriverComponent obsolete — intentional usage` (and the matching `restore`).
- Delete the method starting at line 141 (`public object SysCtl(string driverId, DriverComponent component)`).

### Step 6 — Clean up driver-pack test classes

In `FluentDocker.Tests/CoreTests/Driver/Podman/PodmanCliDriverPackTests.cs`:
- Remove the pragma at line 10.
- Remove each test that calls `pack.SysCtl("podman", DriverComponent.X)` (lines
  71, 117, 181, 196, 219, 242 — six tests). For each, verify there's an
  equivalent generic test like `pack.SysCtl<IContainerDriver>("podman")` already
  in the file. If not, port the test to the generic form rather than just
  deleting (preserves coverage).

In `FluentDocker.Tests/CoreTests/Driver/Docker/DockerCliDriverPackTests.cs`:
- Remove the pragma at line 16.
- Remove the call-site at line 95.
- Remove the `[Theory]`/`[InlineData(DriverComponent.X)]` test starting at
  line 372 (will span ~15 lines). Same rule: port to generic form if no
  equivalent already exists.

### Step 7 — Update docs

In `docs/architecture.md` around line 178 (the section currently titled
something like "Component-based access (legacy)"):
- Delete the entire `DriverComponent` example block.
- Delete the `> Note: DriverComponent is [Obsolete]...` line that was added by
  DOCS_SYNC_PLAN P2.7 at line 180.
- Leave the generic `SysCtl<T>` example intact — that's the now-only-supported path.

In `docs/extensibility.md` around line 94, same treatment: delete the
`DriverComponent.Network` example and the `[Obsolete]` note from P2.7.

If either file ends up under the new prose flow, smooth the transition with
one sentence — don't leave a dangling section header.

## Verification

After Step 4:
```sh
grep -rn "DriverComponent" FluentDocker/ FluentDocker.Testing.*/ FluentDocker.Tests*/ 2>/dev/null | grep -v "/bin/\|/obj/"
```
Expected: zero hits.

After Step 7:
```sh
grep -rn "DriverComponent" docs/ 2>/dev/null
```
Expected: zero hits.

Final:
```sh
make build
make test
```
Both must succeed. **Watch for any leftover `#pragma warning disable CS0618 // DriverComponent`**
— if the build complains about an unused pragma, that's a leftover to remove.

## Rollback

Single revert per step. Steps 2 and 3 must be done together (the kernel
interface no longer requires the method, but if you delete from `ISysCtl`
before deleting from the packs, the packs won't compile — they'll have a
public method that doesn't override anything, which is fine, but it'll still
trigger an obsolete warning if you forgot a pragma).

**Safe order**: Step 5 + 6 (tests first) → Step 3 (impls) → Step 2 (kernel) →
Step 1 (interface) → Step 4 (delete enum) → Step 7 (docs).

## Out of scope

- `IService` removal — REFACTOR_A1.
- `Services.NetworkCreateConfig` — REFACTOR_A3.
- `IFeature` removal — REFACTOR_B.
