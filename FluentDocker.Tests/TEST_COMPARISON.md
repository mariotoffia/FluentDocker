# V2 to V3 Test Comparison

This document tracks the migration status of tests from the v2.x.x system to v3.0.0.

## Legend
- ✅ Implemented - Test exists in v3
- 🆕 New in V3 - New test added for V3 specific features
- ⏳ Needs Port - Test should be ported to V3
- ❌ Deprecated - Test should NOT be ported (deprecated feature)
- 🔄 Refactored - Test concept exists but implementation changed
- 📋 Planned - Test structure defined but not yet implemented

---

## Current V3 Test Structure

```
FluentDocker.Tests/
├── CoreTests/
│   ├── Core/
│   │   ├── AsyncPatternsTests.cs       🆕 New async/await pattern tests
│   │   ├── BuildResultsTests.cs        🆕 Builder results tests
│   │   └── BuildScopeTests.cs          🆕 Builder scope tests
│   ├── Driver/
│   │   ├── CommandResponseTests.cs     🆕 Driver response tests
│   │   ├── DriverRegistryTests.cs      🆕 Driver registry tests
│   │   ├── ErrorContextTests.cs        🆕 Error context tests
│   │   └── ErrorHandlingTests.cs       🆕 Exception handling tests
│   ├── Exceptions/
│   │   └── ExceptionTests.cs           🆕 Custom exception tests
│   ├── Extensions/
│   │   ├── ConversionExtensionTests.cs ✅ Ported from V2
│   │   ├── EnvironmentExtensionTests.cs 🆕 Environment detection tests
│   │   ├── ExtensionUtilityTests.cs    🆕 Utility tests
│   │   └── ResourceExtensionTests.cs   ✅ Ported from V2
│   ├── Model/
│   │   └── TemplateStringTests.cs      ✅ Ported from V2
│   ├── Parser/
│   │   └── NetworkLsResponseParserTests.cs ✅ Ported from V2
│   ├── Process/
│   │   └── ProcessEnvironmentTests.cs  ✅ Ported from V2
│   ├── BuilderTests/
│   │   ├── ContainerBuilderTests.cs    🆕 Container builder tests
│   │   ├── WaitConditionTests.cs       🆕 Wait condition tests
│   │   ├── LifecycleHookTests.cs       🆕 Lifecycle hook tests
│   │   ├── DockerfileBuilderTests.cs   🆕 Dockerfile builder tests
│   │   └── ImageBuilderTests.cs        🆕 Image builder tests
│   └── Service/
│       ├── ContainerServiceTests.cs    🆕 Container service tests
│       ├── NetworkServiceTests.cs      🆕 Network service tests
│       ├── VolumeServiceTests.cs       🆕 Volume service tests
│       └── ComposeServiceTests.cs      🆕 Compose service tests
├── Integration/
│   ├── BuilderTests.cs                 🆕 Builder integration tests
│   └── KernelBuilderTests.cs           🆕 Kernel builder tests
└── Resources/                          ✅ Kept from V2
```

---

## Docker CLI Driver Integration Tests

Integration tests for DockerCliDriver. These require Docker daemon to be running.

```
FluentDocker.Tests/
├── Integration/
│   └── DockerCliDriver/
│       ├── DockerDriverTestBase.cs    ✅ Base class with kernel setup
│       ├── ContainerDriverTests.cs    ✅ Container lifecycle tests (15 tests)
│       ├── NetworkDriverTests.cs      ✅ Network operations tests (9 tests)
│       ├── VolumeDriverTests.cs       ✅ Volume operations tests (8 tests)
│       ├── ImageDriverTests.cs        ✅ Image operations tests (8 tests)
│       └── SystemDriverTests.cs       ✅ System info tests (6 tests)
```

### V2 Test to V3 Driver Test Mapping

| V2 Test | V3 Location | Status |
|---------|-------------|--------|
| DockerClientCommandTests.cs | Integration/DockerCliDriver/ContainerDriverTests.cs | ✅ Ported |
| NetworkCommandTests.cs | Integration/DockerCliDriver/NetworkDriverTests.cs | ✅ Ported |
| VolumeTests.cs | Integration/DockerCliDriver/VolumeDriverTests.cs | ✅ Ported |
| ImageTests.cs | Integration/DockerCliDriver/ImageDriverTests.cs | ✅ Ported |
| DockerInfoCommandTests.cs | Integration/DockerCliDriver/SystemDriverTests.cs | ✅ Ported |
| FluentContainerBasicTests.cs | Integration/DockerCliDriver/ContainerDriverTests.cs | ✅ Ported |
| FluentNetworkTests.cs | Integration/DockerCliDriver/NetworkDriverTests.cs | ✅ Ported |
| FluentVolumeTests.cs | Integration/DockerCliDriver/VolumeDriverTests.cs | ✅ Ported |
| ImageBuilderTests.cs | CoreTests/BuilderTests/DockerfileBuilderTests.cs | ✅ Implemented |
| FluentMultiContainerTests.cs | - | ⏳ Needs builder support |
| RemoteDaemonTests.cs | - | 📋 Planned |

### Known Issues

Some integration tests may fail due to model deserialization mismatches:
- `SystemDriverTests.GetVersion_*` - VersionInfo model needs updating for nested Client/Server structure
- `SystemDriverTests.GetInfo_*` - SystemInfo model needs updating for Runtimes object structure
- Filter tests - Some filter implementations need to handle Docker's JSON format correctly

These are model/parsing issues that can be fixed separately without affecting the test structure.

---

## V2 Tests Status

### 1. CommandTests (Command Layer) - ❌ DEPRECATED
The entire Commands namespace is deprecated. Tests should NOT be ported.

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| ClientStreamCommandTests.cs | ❌ Deprecated | Use IStreamDriver instead |
| ComposeCommandTests.cs | ❌ Deprecated | Use IComposeDriver instead |
| DockerClientCommandTests.cs | ❌ Deprecated | Use IContainerDriver instead |
| DockerInfoCommandTests.cs | ❌ Deprecated | Use ISystemDriver instead |
| DockerMachineCommandTests.cs | ❌ Deprecated | Docker Machine is deprecated |
| ImageTests.cs | ❌ Deprecated | Use IImageDriver instead |
| NetworkCommandTests.cs | ❌ Deprecated | Use INetworkDriver instead |

### 2. Common (Utilities)

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| LoggerTests.cs | ❌ Deprecated | Logger implementation simplified in V3 |

### 3. ExtensionTests

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| CommandExtensionTest.cs | ❌ Deprecated | Commands are deprecated |
| ConversionExtensionTests.cs | ✅ Ported | CoreTests/Extensions/ConversionExtensionTests.cs |
| EnironmentExtensionTests.cs | ✅ Ported | CoreTests/Extensions/EnvironmentExtensionTests.cs |
| ResourceExtensionsTests.cs | ✅ Ported | CoreTests/Extensions/ResourceExtensionTests.cs |

### 4. FluentApiTests (Integration Tests)

| V2 Test File | Status | V3 Equivalent |
|-------------|--------|---------------|
| FluentContainerBasicTests.cs | ⏳ Needs Port | Need Integration/ContainerTests.cs |
| FluentDockerComposeTests.cs | ⏳ Needs Port | Need Integration/ComposeTests.cs |
| FluentMultiContainerTests.cs | ⏳ Needs Port | Need Integration/MultiContainerTests.cs |
| FluentNetworkTests.cs | ⏳ Needs Port | Need Integration/NetworkTests.cs |
| FluentVolumeTests.cs | ⏳ Needs Port | Need Integration/VolumeTests.cs |
| ImageBuilderTests.cs | 🔄 Refactored | Part of BuilderTests.cs |
| IssuesTests.cs | ⏳ Needs Port | Regression tests for issues |
| RemoteDaemonTests.cs | ⏳ Needs Port | Remote docker host tests |
| WaitTests.cs | ⏳ Needs Port | Need Unit/Builder/WaitConditionTests.cs |

### 5. Model/Builders

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| CmdCommandTests.cs | 🔄 Refactored | Tested via DockerfileBuilderTests |
| ContainerBuilderConfigTests.cs | 🔄 Refactored | Config is in V3 builder |
| FileBuilder/CmdCommandTests.cs | 🔄 Refactored | Tested via DockerfileBuilderTests |
| FileBuilder/CopyCommandTests.cs | 🔄 Refactored | Tested via DockerfileBuilderTests |

### 6. Model/Common

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| TemplateStringTests.cs | ✅ Ported | CoreTests/Model/TemplateStringTests.cs |

### 7. Model/Containers

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| ContainerCreateParamsTests.cs | ❌ Deprecated | V2 params replaced by ContainerCreateConfig |
| ContainerTests.cs | 🔄 Refactored | Container model still exists |

### 8. ProcessResponseParsersTests

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| NetworkLsResponseParserTests.cs | ✅ Ported | CoreTests/Parser/NetworkLsResponseParserTests.cs |

### 9. ProcessTests

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| ProcessEnvironmentTest.cs | ✅ Ported | CoreTests/Process/ProcessEnvironmentTests.cs |

### 10. ServiceTests (Service Layer)

| V2 Test File | Status | V3 Equivalent |
|-------------|--------|---------------|
| ContainerServiceBasicTests.cs | ✅ Ported | CoreTests/Service/ContainerServiceTests.cs |
| DockerComposeTests.cs | ✅ Ported | CoreTests/Service/ComposeServiceTests.cs |
| MachineServiceTests.cs | ❌ Deprecated | Docker Machine is deprecated |
| NetworkServiceTests.cs | ✅ Ported | CoreTests/Service/NetworkServiceTests.cs |

---

## Summary Statistics

| Category | Total | Implemented | Deprecated |
|----------|-------|-------------|------------|
| Command Tests | 7 | 0 | 7 (ported to Driver tests) |
| Common | 1 | 0 | 1 |
| Extension Tests | 4 | 4 | 0 |
| FluentApi Tests | 9 | 4 | 1 |
| Model Tests | 6 | 5 | 1 |
| Parser Tests | 1 | 1 | 0 |
| Process Tests | 1 | 1 | 0 |
| Service Tests | 4 | 3 | 1 |
| **V3 Unit Tests** | 328 | 328 | - |
| **Docker Driver Tests** | 56 | 46 | - |
| **TOTAL** | 400+ | 392 | 11 |

### Test Implementation Status
- ✅ **Unit Tests**: 328 tests fully implemented (all passing)
- ✅ **Docker Driver Integration Tests**: 46 passing, 10 need model fixes
- ✅ **DockerfileBuilder**: Full support for programmatic Dockerfile creation
- ✅ **ImageBuilder**: Full support for building Docker images
- ✅ **Extension Tests**: ConversionExtension, ResourceExtension, EnvironmentExtension ported
- ✅ **Model Tests**: TemplateString tests ported
- ✅ **Parser Tests**: NetworkLsResponseParser tests ported
- ✅ **Process Tests**: ProcessEnvironment tests ported
- ✅ **Container Driver Tests**: Full lifecycle tests (run, stop, pause, exec, etc.)
- ✅ **Network Driver Tests**: Create, connect, disconnect tests
- ✅ **Volume Driver Tests**: Create, mount, list tests
- ✅ **Image Driver Tests**: Pull, inspect, tag tests
- ⚠️ **System Driver Tests**: Need model updates for Docker JSON structure

---

## Notes

### What Changed in V3

1. **Commands → Drivers**: All direct Docker command calls are now through driver interfaces
2. **Sync → Async**: All operations are now async-first with sync wrappers
3. **Static Hosts → Kernel**: Static `Hosts` class replaced by `FluentDockerKernel`
4. **Builder pattern**: New terminal `BuildAsync()` pattern with `WithinDriver()` scoping
5. **Services**: New async service interfaces with proper disposal

### Deprecated Features (Do NOT port tests)

1. Docker Machine - Fully deprecated by Docker
2. Docker Toolbox - Deprecated by Docker Desktop
3. Direct command execution - Use drivers instead
4. Sync-only operations - All operations are now async


