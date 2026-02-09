# FluentDocker v3.0 Migration TODO List

**Created**: 2026-01-27
**Status**: Complete (26/26 tasks done)

---

## Phase 1: Must Do (Blocks v3.0 Release)

### Removal Tasks
- [x] **1. Remove IMachineDriver interface and implementation** ✅
  - Removed: `FluentDocker/Drivers/IMachineDriver.cs`
  - Removed: `FluentDocker/Drivers/DockerCli/DockerCliMachineDriver.cs`
  - Removed from: `DockerCliDriverPack.cs`
  - Removed: Machine parsers, Machine models, Binary resolver Machine support
  - Updated: All references in documentation (README.md, csproj, comments)

- [x] **2. REMOVE Commands namespace completely** ✅
  - Already removed: `FluentDocker/Commands/` directory does not exist
  - Verified: No namespace or using references remain
  - Note: Commands layer was replaced by Driver layer in v3.0

- [x] **3. Remove DOCKER_TOOLBOX environment detection code** ✅
  - Removed: DOCKER_TOOLBOX_INSTALL_PATH environment check from DockerUri.cs
  - Removed: ConfigureToolboxResolver and IsToolboxFromEnvironment from DockerUri.cs
  - Removed: Toolbox references from DockerBinariesResolver.cs
  - Updated: All related comments and documentation

- [x] **4. Remove docker-compose binary support** ✅
  - DockerCliComposeDriver.cs uses "compose" subcommand (Compose V2)
  - DockerBinariesResolver.cs redirects "docker-compose" to ComposeV2
  - DockerBinary.Translate() maps "docker-compose" to DockerBinaryType.ComposeV2
  - Note: docker-compose.yml file references are fine (file format name)

### Porting Tasks
- [x] **5. Port directory copy support (CopyToAsync recursive)** ✅
  - Added: `CopyToAsync(string hostPath, string containerPath)` to IContainerService
  - Added: `CopyFromToPathAsync(string containerPath, string hostPath)` to IContainerService
  - Updated: `ExecuteLifecycleHooksAsync` to use path-based methods
  - Uses docker cp which natively supports both files and directories

- [x] **6. Port Static IPv4/IPv6 assignment (UseIpV4/UseIpV6)** ✅
  - Added: `Ipv4Address` and `Ipv6Address` to ContainerCreateConfig
  - Added: `UseIpV4(string)` and `UseIpV6(string)` to IContainerBuilder
  - Updated: ContainerBuilder implementation with private fields and methods
  - Updated: DockerCliContainerDriver to use `--ip` and `--ip6` flags

- [x] **7. Port Wget HTTP helper extension** ✅
  - Already exists: `FluentDocker/Extensions/HttpExtensions.cs`
  - Methods: `Wget(this string url)`, `DoRequest(...)`, `Download(this Uri, string)`
  - Signature: `Task<string> url.Wget()` returns response body or empty string

- [x] **8. Port ResourceExtract utility** ✅
  - Already exists: `FluentDocker/Extensions/ResourceExtensions.cs`
  - Method: `ResourceExtract(this Type, TemplateString targetPath, params string[] files)`
  - Also: `ResourceQuery(this Type)`, `ToFile(this IEnumerable<ResourceInfo>)`, `ToFile(this EmbeddedUri)`

- [x] **9. Port TemplateString (verify and unit test)** ✅
  - Location: `FluentDocker/Model/Common/TemplateString.cs`
  - Verified: Full implementation with ${TEMP}, ${TMP}, ${RND}, ${PWD}, ${E_*}
  - Tests: `FluentDocker.Tests/CoreTests/Model/TemplateStringTests.cs` (17 tests)

- [x] **10. Implement Container Stats (GetStatsAsync)** ✅
  - Added: `StatsAsync` to `IContainerDriver` interface
  - Added: `ContainerStatsResult` model class
  - Implemented: `DockerCliContainerDriver.StatsAsync` using `docker stats --no-stream --format json`
  - Updated: `ContainerService.GetStatsAsync` to use driver
  - Added: `ErrorCodes.Container.StatsFailed` error code

### Testing
- [x] **11. Create unit tests for all ported features** ✅
  - Added: `ContainerBuilderTests` - UseIpV4/UseIpV6 tests (3 tests)
  - Added: `HttpExtensionTests` - Wget HTTP helper tests (8 tests)
  - Added: `ContainerStatsParsingTests` - Stats parsing tests (12 tests)
  - Added: `ContainerServiceTests.GetStatsAsync_CallsDriverAndReturnsStats`
  - Added: `MockDriverPack.SetupContainerStats` for mocking
  - Verified: 534 unit tests pass

- [x] **12. Create integration tests for ported features** ✅
  - Added: `Stats_RunningContainer_ReturnsResourceUsage` - Container stats test
  - Added: `CopyTo_FileToContainer_Succeeds` - File copy to container test
  - Added: `CopyFrom_FileFromContainer_Succeeds` - File copy from container test
  - Added: `CopyTo_DirectoryToContainer_Succeeds` - Directory copy test
  - Added: `Run_WithStaticIPv4_AssignsIPAddress` - Static IP assignment test
  - Fixed: `--ip` and `--ip6` flags in `RunAsync` method
  - Fixed: Exec command argument quoting with `QuoteArgumentIfNeeded`
  - Verified: 656 tests pass (122 integration + 534 unit)

- [x] **13. Create benchmarks for new features** ✅
  - Added: `FluentDocker.Benchmarks` project with BenchmarkDotNet 0.14.0
  - Added: `ContainerStatsBenchmarks.cs` - JSON parsing performance tests
  - Added: `TemplateStringBenchmarks.cs` - Path template interpolation tests
  - Added: `HttpExtensionBenchmarks.cs` - URL parsing/manipulation tests
  - Added: Solution integration (`dotnet sln add`)
  - Updated: Makefile with `make benchmark` targets

- [x] **14. Run make test successfully** ✅
  - All 659 tests pass
  - Fixed: Static IP test subnet conflict (changed from 172.28.0.0/16 to 10.199.0.0/16)
  - Fixed: WordPress compose port conflict (created docker-compose-test.yml with dynamic ports)
  - No regressions

### Documentation
- [x] **15. Update MIGRATE_TO_V3.md** ✅
  - Added: IMachineDriver removal notice (Docker Machine section)
  - Added: Commands namespace complete removal (changed from deprecated to removed)
  - Added: New ported features (Stats, Directory Copy, IPv6, Wget, TemplateString)
  - Updated: Breaking changes summary table
  - Updated: Migration checklist
  - File reduced from 812 to 258 lines (under 600 limit)

---

## Phase 2: Should Do (Clean v3.0)

- [x] **16. Update docs/index.md** ✅
  - Marked deprecated features (machine, toolbox, docker-compose)
  - Added v3 migration notice at top
  - Updated all examples to use FluentDocker namespace
  - Added new v3 features documentation
  - File reduced from 934 to 407 lines (under 600 limit)

---

## Phase 3: Nice to Have (Can Defer to v3.1)

- [x] **17. Podman CLI Driver** ✅
  - Phase 1 (Core Compatibility): Container, Image, Network, Volume, System, Auth, Stream drivers
  - Pod Driver: Full lifecycle + inspect (IPodmanPodDriver)
  - Kubernetes Integration: play, down, generate (IPodmanKubernetesDriver)
  - Machine Management: init, start, stop, rm, list, inspect, ssh, set, info (IPodmanMachineDriver)
  - Manifest/Multi-Arch: create, add, push, inspect, annotate, rm, exists (IPodmanManifestDriver)
  - 16 source files in `FluentDocker/Drivers/Podman/Cli/`

- [x] **18. Docker API Driver** ✅
  - 19 source files in `FluentDocker/Drivers/Docker/Api/`
  - Connection layer: Unix socket, named pipe, TCP+TLS with API version auto-negotiation
  - 8 component drivers: Container, Image, Network, Volume, System, Auth, Stream, Service
  - Docker multiplexed stream protocol (8-byte header) for logs/attach
  - NDJSON streaming for events/stats/pull/push/build
  - Tar archive via SharpCompress for copy-to/copy-from and image build
  - `KernelBuilder.UseDockerApi()` wired to `DockerApiDriverPack`
  - 224 unit tests, 10 integration tests, 5 benchmark categories

- [x] **19. Create GitHub Pages documentation site** ✅
  - Created: `docs/_config.yml` with just-the-docs theme
  - Created: `docs/Gemfile` for Jekyll dependencies
  - Created: `.github/workflows/pages.yml` for automatic deployment
  - Added: Front matter to `docs/index.md` and `docs/architecture/README.md`
  - Created: `docs/migration.md` - Migration guide page
  - Updated: README.md with link to GitHub Pages site
  - Site URL: https://mariotoffia.github.io/FluentDocker/

- [x] **20. Update README.md** ✅
  - Added "What's New in v3.0.0" highlights section
  - Added Breaking Changes section
  - Updated all examples to use FluentDocker namespace
  - Replaced SonarCloud badge with Codecov badge
  - File reduced from 1181 to 323 lines (under 600 limit)

- [x] **21. Verify Makefile completeness** ✅
  - Setup target: `make dep`
  - Clean target: `make clean`
  - Lint/Vet target: `make lint`, `make format`
  - Test target: `make test`, `make test-unit`, `make test-integration`
  - Benchmark target: `make benchmark`, `make benchmark-stats`, `make benchmark-template`

---

## Phase 4: Technical Debt (Low Priority)

- [x] **22. Fix ModelExtensions edge cases (MountType)** ✅
  - Location: `ModelExtensions.cs`
  - Changed: NotImplementedException to ArgumentOutOfRangeException
  - Updated: Switch statement to modern switch expression

- [x] **23. Clean up ResourceQuery hack** ✅
  - Location: `ResourceQuery.cs`
  - Replaced "ugly hack" comment with proper XML documentation
  - Explained why string-based extraction is necessary (no .NET API for this)
  - Cleaned up variable naming (extensionDot, extensionLength)
  - Removed unused variable in Query() method

- [x] **24. Update/Add Examples** ✅
  - Updated: All existing examples to target net10.0
  - Added: `ContainerStats` example
    - Container resource monitoring (CPU, memory, network, disk)
    - Static IPv4 assignment with custom networks
  - Added: `ComposeV2` example
    - Docker Compose V2 usage
    - Directory copy to/from containers
    - TemplateString path interpolation
  - Updated: Examples/README.md with all example descriptions

- [x] **25. Replace SonarCloud with GitHub-native code scanning** ✅
  - Removed: SonarCloud integration from CI workflow
  - Added: GitHub Code Scanning (CodeQL) for security/quality analysis
  - Added: Codecov for code coverage (requires CODECOV_TOKEN secret)
  - Added: CodeQL job runs on PRs and main branches
  - Updated: .NET SDK versions to 8.0.x and 10.0.x
  - Note: Add coverage badge to README.md when Codecov is configured

- [x] **26. Consolidate architecture documentation** ✅
  - Created: `docs/architecture.md` (446 lines) - Driver layer, kernel config, async patterns
  - Created: `docs/error_handling.md` (564 lines) - Exception hierarchy, error codes, diagnostics
  - Removed: `docs/architecture/` folder (12 internal planning documents)
  - Ported public-facing content, kept internal docs (IMPLEMENTATION_PLAN, TEST_PLAN) removed
  - Site now has consolidated, user-friendly documentation

---

## Not Planned (Low Priority, On-Demand)

These Podman features are not planned for implementation:

- **Pod monitoring** (`podman pod logs/top/stats`): Niche monitoring, use container-level stats instead
- **Secret management** (`podman secret`): Low adoption, consider on demand
- **Healthcheck** (`podman healthcheck run`): Embedded in container lifecycle, manual run is edge case
- **Generation** (`podman generate systemd/spec`): Linux-specific, consider on demand
- **Advanced** (`podman mount/unmount/unshare/auto-update/farm/artifact/quadlet`): Niche/Linux-specific

---

## Notes

- Commands namespace: **COMPLETELY REMOVED** in v3.0 (user decision 2026-01-27)
- Per CLAUDE.md: Must run `make test` successfully before completion
- Per CLAUDE.md: Must create unit tests, integration tests, AND benchmarks


---

## DockerApi Driver Test Coverage (Complete)

| Driver | Unit Tests | Status |
|--------|-----------|--------|
| Network | 16 | Complete |
| Volume | 13 | Complete |
| System | 20 | Complete |
| Stream | 14 | Complete (incl. multiplexed frame parsing) |
| Driver Pack | 33 | Complete |
| Image | 24 | Complete (incl. push, build, save, load, import) |
| Container | 45 | Complete (incl. ops: exec, copy, logs, top, diff, export, rename, update) |
| Service | 24 | Complete |
| Auth | 6 | Complete |
| Connection | 11 | Complete |
| Error Mapping | 20 | Complete |
| **Total** | **224** | **All pass** |
