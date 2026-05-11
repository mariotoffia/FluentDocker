# Refactor A1 — Remove `IService` (sync interface)

Plan date: 2026-05-11
Scope: complete the v3 deprecation by deleting the `[Obsolete]` sync surface
that `IServiceAsync` currently inherits from. This is the highest-blast-radius
item from the `[Obsolete]` audit.

## Why

`IServiceAsync : IService` (see `FluentDocker/Services/IServiceAsync.cs:14`)
forces every async service to also provide synchronous `Start/Pause/Stop/Remove`
plus a sync `AddHook(Action<IService>)` overload. The sync implementations
internally call `.GetAwaiter().GetResult()`, which deadlocks on single-threaded
sync contexts (UI thread, classic ASP.NET). The deprecation note on
`IService.cs:18` already says "will be removed in v4" — this plan brings that
forward to v3.

## Outcome

- `IService` interface deleted.
- `IServiceAsync` is the root service interface (extends only
  `IAsyncDisposable`, plus `IDisposable` if we want to keep deterministic sync
  dispose).
- All 7 service implementations lose: sync `Start/Pause/Stop/Remove`, explicit
  `IService IService.AddHook(...)`, `IService IService.RemoveHook(...)`.
- `BuildResults.All` / `ForDriver` / `OfType<T>` change from `IService` to
  `IServiceAsync`.
- All 11 `#pragma warning disable CS0618 // IService obsolete` directives
  removed.
- `ServiceDelegates.StateChange` delegate type **kept** (it's the event
  signature, unrelated to the sync/async split); event declaration moves from
  `IService` to `IServiceAsync`.

## Files affected

### Source (FluentDocker)

| File | Change kind |
|---|---|
| `FluentDocker/Services/IService.cs` | **DELETE** entire file (after extracting `ServiceDelegates` class) |
| `FluentDocker/Services/IServiceAsync.cs` | Add `StateChange` event + `Name` + `State` + `IDisposable` inheritance; drop `: IService` |
| `FluentDocker/Services/StateChangeEventArgs.cs` | Drop pragma at line 3 |
| `FluentDocker/Services/Impl/ContainerService.cs` | Remove sync method impls + explicit `IService.*` impls |
| `FluentDocker/Services/Impl/ComposeService.cs` | Same |
| `FluentDocker/Services/Impl/NetworkService.cs` | Same |
| `FluentDocker/Services/Impl/VolumeService.cs` | Same + drop pragma at line 11 |
| `FluentDocker/Services/Impl/HostService.cs` | Same |
| `FluentDocker/Services/Impl/ImageService.cs` | Same |
| `FluentDocker/Services/Impl/PodService.cs` | Same |
| `FluentDocker/Model/Kernel/BuildResults.cs` | Constraint + return types `IService` → `IServiceAsync`; drop pragma at line 7 |
| `FluentDocker/Model/Kernel/BuildScope.cs` | Drop pragma at line 8 + any `IService` references |
| `FluentDocker/Builders/Builder.cs` | Drop pragma at line 12 |
| `FluentDocker/Builders/ContainerBuilder.cs` | Drop pragma at line 15 |
| `FluentDocker/Builders/InternalBuilders.cs` | Drop pragma at line 11 |
| `FluentDocker/Builders/PodBuilder.cs` | Drop pragma at line 10 |
| `FluentDocker/Model/IFeature.cs` | Will be deleted by REFACTOR_B (no action here) |

### Tests (FluentDocker.Tests)

| File | Change kind |
|---|---|
| `FluentDocker.Tests/CoreTests/Core/BuildResultsTests.cs` | Update mock at line 179 — drop sync `AddHook(Action<IService>)`, change `IService` references |
| `FluentDocker.Tests/CoreTests/Core/BuildScopeTests.cs` | Same — line 127, 130 |
| `FluentDocker.Tests/CoreTests/Service/ContainerServiceTests.cs` | No code changes — already uses async hook signature |
| `FluentDocker.Tests/CoreTests/Service/ComposeServiceTests.Lifecycle.cs` | No code changes |

### Docs

| File | Change kind |
|---|---|
| `docs/architecture.md` | Update any `IService` mention to `IServiceAsync` |
| `docs/migrate-v2-to-v3/api-mapping.md` | Same |
| `docs/containers.md` | Remove the "sync overloads still work but `[Obsolete]`" parentheticals added in P3.13 of `DOCS_SYNC_PLAN.md` (~lines 74, 95) — they're no longer accurate |

## Tasks

### Step 1 — Move `ServiceDelegates` out of `IService.cs` and update `IServiceAsync.cs`

`IService.cs:9-13` currently defines:
```csharp
public sealed class ServiceDelegates
{
  public delegate void StateChange(object sender, StateChangeEventArgs evt);
}
```

Move `ServiceDelegates` to a new file `FluentDocker/Services/ServiceDelegates.cs`
(or fold it into `StateChangeEventArgs.cs` — same namespace).

Then update `IServiceAsync.cs` so it no longer inherits `IService`:

```csharp
public interface IServiceAsync : IDisposable, IAsyncDisposable
{
  string Name { get; }
  ServiceRunningState State { get; }

  Task StartAsync(CancellationToken cancellationToken = default);
  Task PauseAsync(CancellationToken cancellationToken = default);
  Task StopAsync(CancellationToken cancellationToken = default);
  Task RemoveAsync(bool force = false, CancellationToken cancellationToken = default);

  IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string uniqueName = null);
  IServiceAsync RemoveHook(string uniqueName);

#pragma warning disable CA1710
  event ServiceDelegates.StateChange StateChange;
#pragma warning restore CA1710
}
```

Drop `#pragma warning disable CS0618` at `IServiceAsync.cs:13`. Drop the
`new IServiceAsync RemoveHook` keyword (no longer hides anything).

### Step 2 — Delete `IService.cs`

After Step 1, `FluentDocker/Services/IService.cs` is dead. Delete it.

### Step 3 — Strip sync surface from each service impl

For each of `ContainerService.cs`, `ComposeService.cs`, `NetworkService.cs`,
`VolumeService.cs`, `HostService.cs`, `ImageService.cs`, `PodService.cs`:

1. Remove the public sync method bodies: `public void Start()`,
   `public void Pause()`, `public void Stop()`, `public void Remove(bool)`.
   Verify each by `grep -n "public void Start\|public void Pause\|public void Stop\|public void Remove(bool" <file>`.
2. Remove the explicit interface impls `IService IService.AddHook(...)` and
   `IService IService.RemoveHook(...)`. These are at lines
   `ContainerService.cs:291`, `ComposeService.cs:283`, `NetworkService.cs:169`,
   `VolumeService.cs:132`, `HostService.cs:304`, `ImageService.cs:198`,
   `PodService.cs:109` (line numbers will drift as you edit — re-grep before
   editing).
3. Keep the `public event ServiceDelegates.StateChange StateChange;` declaration
   (now satisfies `IServiceAsync`, not `IService`).
4. Remove `#pragma warning disable CS0618` in `VolumeService.cs:11`.

### Step 4 — Update `BuildResults.cs` and `BuildScope.cs`

In `FluentDocker/Model/Kernel/BuildResults.cs`:

- Line 7: remove `#pragma warning disable CS0618`.
- Line 26: change `IReadOnlyList<IService> All` → `IReadOnlyList<IServiceAsync> All`.
- Line 34: change `IReadOnlyList<IService> ForDriver(...)` →
  `IReadOnlyList<IServiceAsync> ForDriver(...)`.
- Line 102: change `where T : IService` → `where T : IServiceAsync`.
- The `Dispose()` (line 136) / `DisposeAll()` (line 145) sync wrappers can stay
  — they belong to `IDisposable`, not `IService`.

In `FluentDocker/Model/Kernel/BuildScope.cs`:
- Line 8: drop pragma. Update any `IService` reference to `IServiceAsync`.

### Step 5 — Drop pragmas in builders

Open each of the 4 builder files and remove the `#pragma warning disable CS0618 // IService obsolete`
line plus its matching `restore` if present:

- `FluentDocker/Builders/Builder.cs:12`
- `FluentDocker/Builders/ContainerBuilder.cs:15`
- `FluentDocker/Builders/InternalBuilders.cs:11`
- `FluentDocker/Builders/PodBuilder.cs:10`

Confirm no code in those files actually requires the suppression — they were
inherited because the builder code touched `IService`-typed parameters now
gone. Build to verify.

Also `FluentDocker/Services/StateChangeEventArgs.cs:3` — drop the pragma there.

### Step 6 — Update test mocks in `BuildResultsTests.cs` / `BuildScopeTests.cs`

The mock class around `BuildResultsTests.cs:179` and `BuildScopeTests.cs:127`
currently implements:
```csharp
public IService AddHook(ServiceRunningState state, Action<IService> hook, string? uniqueName = null) => this;
```

After A1 that interface method doesn't exist. Change the mock to implement
`IServiceAsync` only:
```csharp
public IServiceAsync AddHook(ServiceRunningState state, Func<IServiceAsync, Task> hook, string? uniqueName = null) => this;
public IServiceAsync RemoveHook(string? uniqueName) => this;
```

Adjust the `event ServiceDelegates.StateChange StateChange;` mock declaration
to keep compiling (it already exists at `BuildResultsTests.cs:182` and
`BuildScopeTests.cs:130`).

### Step 7 — Update docs

In `docs/architecture.md` and `docs/migrate-v2-to-v3/api-mapping.md`:
search for any `IService` mention (without the `Async` suffix). Replace with
`IServiceAsync` unless the prose is specifically explaining the v2→v3 deletion.
If it is, change it from "deprecated, removed in v4" to "removed in v3".

In `docs/containers.md` at ~lines 74 and 95 (added during DOCS_SYNC_PLAN P3.13):
remove the parenthetical "The sync `Stop()` / `Start()` overloads still work
but are `[Obsolete]` in v3 — prefer the `Async` forms on `IServiceAsync`." —
there are no sync overloads anymore. Remove the whole sentence; the rest of
those paragraphs already shows async forms as primary.

## Verification

After each step, run:
```sh
grep -rn "\bIService\b" FluentDocker/ FluentDocker.Testing.*/ FluentDocker.Tests*/ | grep -v "IServiceAsync\|IServiceCapabilities\|/bin/\|/obj/"
```
Expected: zero hits in source after Step 5; only test-mock declarations remain
until Step 6.

```sh
grep -rn "CS0618.*IService\b\|IService.*CS0618" FluentDocker/ FluentDocker.Testing.*/ FluentDocker.Tests*/ | grep -v "/bin/\|/obj/"
```
Expected: zero hits after Step 5.

Final:
```sh
make build
make test
```
Both must succeed with **zero CS0618 warnings related to `IService`** in the
build output.

## Rollback

Each step is a single commit; revert in reverse order if a step breaks the
build beyond simple fix-up.

## Out of scope (do NOT touch in this refactor)

- `DriverComponent` enum / `ISysCtl.SysCtl(string, DriverComponent)` — see REFACTOR_A2_PLAN.
- `Services.NetworkCreateConfig` duplicate type — see REFACTOR_A3_PLAN.
- `IFeature` / `FeatureAttribute` dead code — see REFACTOR_B_PLAN.
- `UseIpV4` / `UseIpV6` / `ResuorceQuery` / `ImageFocrePull` / `Domainname`
  typo aliases — handled inline (see C1–C4 in conversation context).
