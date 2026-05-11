# Refactor A3 — Remove `Services.NetworkCreateConfig` duplicate

Plan date: 2026-05-11
Scope: complete the v3 deprecation by deleting the `[Obsolete]`
`FluentDocker.Services.NetworkCreateConfig` class that duplicates
`FluentDocker.Drivers.NetworkCreateConfig`.

## Why

`Services.NetworkCreateConfig` (see `FluentDocker/Services/IHostService.cs:215`)
is a v2 holdover. The canonical type now lives at
`FluentDocker/Drivers/INetworkDriver.cs:136` as
`FluentDocker.Drivers.NetworkCreateConfig`. The Services version exists only
because `IHostService.CreateNetworkAsync` exposes it as a parameter type;
internally `HostService.Operations.cs` translates it to the Drivers version
field-by-field with a `#pragma warning disable CS0618` suppression.

Removing the duplicate makes `IHostService.CreateNetworkAsync` take the
canonical Drivers type directly, dropping the mapping code and two pragmas.

## Outcome

- `Services.NetworkCreateConfig` class deleted (top of `IHostService.cs`
  removed; class definition at line 213 onwards removed).
- `IHostService.CreateNetworkAsync` signature changes to take
  `FluentDocker.Drivers.NetworkCreateConfig` (a one-token namespace swap).
- `HostService.Operations.CreateNetworkAsync` impl simplifies — no more
  Services→Drivers field copying.
- Two `#pragma warning disable CS0618` directives removed.

## Files affected

### Source (FluentDocker)

| File | Change kind |
|---|---|
| `FluentDocker/Services/IHostService.cs` | Drop pragma (line 7); change `CreateNetworkAsync` param type (line ~116); delete obsolete class (lines 213–end of class) |
| `FluentDocker/Services/Impl/HostService.Operations.cs` | Drop pragma (line 10); change `CreateNetworkAsync` param type (line ~158); delete the `Services→Drivers` mapping block (lines ~164–175); pass `config` (typed as Drivers) directly to the driver |

### Tests

| File | Change kind |
|---|---|
| `FluentDocker.Tests/CoreTests/Service/HostServiceTests.cs` (if it exercises `CreateNetworkAsync`) | Update any test that constructs `Services.NetworkCreateConfig` — switch to `Drivers.NetworkCreateConfig` |

Search first:
```sh
grep -rn "Services\.NetworkCreateConfig\|new NetworkCreateConfig\b" FluentDocker.Tests/ 2>/dev/null | grep -v "/bin/\|/obj/"
```

### Docs

| File | Change kind |
|---|---|
| `docs/` | (grep — there are no doc references to `Services.NetworkCreateConfig` per the audit. Confirm with the grep below.) |

```sh
grep -rln "Services\.NetworkCreateConfig" docs/ 2>/dev/null
```
Expected: zero hits.

## Tasks

### Step 1 — Update `IHostService.cs`

Open `FluentDocker/Services/IHostService.cs`:

1. Line 7: remove `#pragma warning disable CS0618 // Services.NetworkCreateConfig obsolete — intentional usage in public API` (and matching `restore` if present).
2. Line ~116 (`Task<INetworkService> CreateNetworkAsync(string name, NetworkCreateConfig config = null, CancellationToken cancellationToken = default);`):
   - Change the type to `FluentDocker.Drivers.NetworkCreateConfig`. Since the
     file already references `FluentDocker.Drivers` namespace (the audit shows
     `using FluentDocker.Drivers;` is in many call-sites), either add a
     `using FluentDocker.Drivers;` at the top or fully qualify the parameter.
   - Recommended: add `using Drivers = FluentDocker.Drivers;` and write
     `Drivers.NetworkCreateConfig config = null` so the parameter type is
     unambiguous and the diff is small.
3. Lines 213 to the end of the obsolete class: delete the entire
   `[Obsolete] public class NetworkCreateConfig { ... }` block, including its
   properties and any helper code that lives inside it.

### Step 2 — Update `HostService.Operations.cs`

Open `FluentDocker/Services/Impl/HostService.Operations.cs`:

1. Line 10: remove `#pragma warning disable CS0618 // Services.NetworkCreateConfig obsolete — intentional internal usage` (and matching `restore`).
2. Line ~158: change `CreateNetworkAsync` parameter type the same way as in
   the interface.
3. Lines ~164–175: this block currently reads:
   ```csharp
   config ??= new NetworkCreateConfig();

   var driverConfig = new Drivers.NetworkCreateConfig
   {
     Name = name,
     Driver = config.Driver,
     Internal = config.Internal,
     EnableIPv6 = config.EnableIPv6,
     Labels = config.Labels ?? new Dictionary<string, string>(),
     Options = config.Options ?? new Dictionary<string, string>()
   };

   var response = await driver.CreateAsync(context, driverConfig, cancellationToken).ConfigureAwait(false);
   ```

   Replace with:
   ```csharp
   config ??= new Drivers.NetworkCreateConfig();
   config.Name = name;

   var response = await driver.CreateAsync(context, config, cancellationToken).ConfigureAwait(false);
   ```

   **Carefully verify** that every field that was being mapped (`Driver`,
   `Internal`, `EnableIPv6`, `Labels`, `Options`) exists on
   `Drivers.NetworkCreateConfig` with the same name and type. The audit
   confirms `Drivers.NetworkCreateConfig` is the canonical version, but
   double-check defaults — e.g. `Driver = "bridge"` may differ.

### Step 3 — Update tests

```sh
grep -rn "Services\.NetworkCreateConfig\|new NetworkCreateConfig\b" FluentDocker.Tests/ 2>/dev/null | grep -v "/bin/\|/obj/"
```

For each hit:
- If the test instantiates `Services.NetworkCreateConfig`, swap to
  `Drivers.NetworkCreateConfig`. Field set should be 1:1.
- If the test imports `using FluentDocker.Services;` and relies on
  unqualified `NetworkCreateConfig`, add `using FluentDocker.Drivers;` (or
  use the alias).
- Remove any `#pragma warning disable CS0618` that was only protecting the
  Services version of the type.

### Step 4 — Confirm docs

Run:
```sh
grep -rn "Services\.NetworkCreateConfig" docs/
```
If non-empty, follow the same swap rules. The audit showed zero docs hits, so
this step is typically a no-op.

## Verification

```sh
grep -rn "Services\.NetworkCreateConfig\|FluentDocker\.Services\.NetworkCreateConfig" \
  FluentDocker/ FluentDocker.Testing.*/ FluentDocker.Tests*/ docs/ 2>/dev/null \
  | grep -v "/bin/\|/obj/"
```
Expected: zero hits.

```sh
grep -rn "CS0618.*NetworkCreateConfig\|NetworkCreateConfig.*CS0618" \
  FluentDocker/ FluentDocker.Testing.*/ FluentDocker.Tests*/ 2>/dev/null \
  | grep -v "/bin/\|/obj/"
```
Expected: zero hits.

Final:
```sh
make build
make test
```
Both must succeed.

## Rollback

Single revert. Safe to do in one commit since the change is small and
field-symmetric — both types have the same properties.

## Out of scope

- `IService` removal — REFACTOR_A1.
- `DriverComponent` removal — REFACTOR_A2.
- `IFeature` removal — REFACTOR_B.
