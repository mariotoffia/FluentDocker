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
│   │   ├── EnvironmentExtensionTests.cs 🆕 Environment detection tests
│   │   └── ExtensionUtilityTests.cs    🆕 Utility tests
│   ├── BuilderTests/
│   │   ├── ContainerBuilderTests.cs    🆕 Container builder tests
│   │   ├── WaitConditionTests.cs       🆕 Wait condition tests
│   │   └── LifecycleHookTests.cs       🆕 Lifecycle hook tests
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

## Planned Docker CLI Driver Integration Tests

The following test structure is planned for Docker CLI driver integration tests.
These require Docker to be running and test actual container/network/volume operations.

```
FluentDocker.Tests/
├── Drivers/
│   └── DockerCliDriverTests/
│       ├── DockerTestBase.cs           📋 Base class with kernel setup
│       ├── ContainerTests/
│       │   ├── BasicContainerTests.cs  📋 Ports from FluentContainerBasicTests.cs
│       │   ├── PortMappingTests.cs     📋 Port mapping scenarios
│       │   └── CustomResolverTests.cs  📋 Custom endpoint resolver tests
│       ├── NetworkTests/
│       │   ├── NetworkBasicTests.cs    📋 Ports from FluentNetworkTests.cs
│       │   └── NetworkContainerTests.cs 📋 Container-network interaction
│       ├── VolumeTests/
│       │   └── VolumeBasicTests.cs     📋 Ports from FluentVolumeTests.cs
│       ├── ComposeTests/
│       │   ├── ComposeBasicTests.cs    📋 Ports from FluentDockerComposeTests.cs
│       │   └── ComposePauseResumeTests.cs 📋 Pause/resume scenarios
│       ├── WaitTests/
│       │   ├── WaitForPortTests.cs     📋 Port wait conditions
│       │   ├── WaitForHealthyTests.cs  📋 Health check waits
│       │   └── WaitLambdaTests.cs      📋 Ports from WaitTests.cs
│       └── RegressionTests/
│           └── IssueTests.cs           📋 Ports from IssuesTests.cs
```

### V2 Test to V3 Driver Test Mapping

| V2 Test | V3 Location | Status |
|---------|-------------|--------|
| FluentContainerBasicTests.cs | ContainerTests/BasicContainerTests.cs | 📋 Planned |
| FluentNetworkTests.cs | NetworkTests/NetworkBasicTests.cs | 📋 Planned |
| FluentVolumeTests.cs | VolumeTests/VolumeBasicTests.cs | 📋 Planned |
| FluentDockerComposeTests.cs | ComposeTests/ComposeBasicTests.cs | 📋 Planned |
| WaitTests.cs | WaitTests/WaitLambdaTests.cs | 📋 Planned |
| IssuesTests.cs | RegressionTests/IssueTests.cs | 📋 Planned |
| FluentMultiContainerTests.cs | - | ❌ Deprecated (was ignored) |
| ImageBuilderTests.cs | - | ❌ Deprecated (FileBuilder deprecated) |
| RemoteDaemonTests.cs | - | 📋 Planned (Remote host tests) |

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
| LoggerTests.cs | ⏳ Needs Port | Still relevant for logging |

### 3. ExtensionTests

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| CommandExtensionTest.cs | ❌ Deprecated | Commands are deprecated |
| ConversionExtensionTests.cs | ⏳ Needs Port | Still relevant |
| EnironmentExtensionTests.cs | 🔄 Refactored | See EnvironmentExtensions in V3 |
| ResourceExtensionsTests.cs | ⏳ Needs Port | Resource handling still needed |

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
| CmdCommandTests.cs | ❌ Deprecated | Dockerfile builder deprecated |
| ContainerBuilderConfigTests.cs | 🔄 Refactored | Config is in V3 builder |
| FileBuilder/CmdCommandTests.cs | ❌ Deprecated | Dockerfile builder deprecated |
| FileBuilder/CopyCommandTests.cs | ❌ Deprecated | Dockerfile builder deprecated |

### 6. Model/Common

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| TemplateStringTests.cs | ⏳ Needs Port | If TemplateString still exists |

### 7. Model/Containers

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| ContainerCreateParamsTests.cs | ❌ Deprecated | V2 params replaced by ContainerCreateConfig |
| ContainerTests.cs | 🔄 Refactored | Container model still exists |

### 8. ProcessResponseParsersTests

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| NetworkLsResponseParserTests.cs | ⏳ Needs Port | Parsers still used by CLI driver |

### 9. ProcessTests

| V2 Test File | Status | Notes |
|-------------|--------|-------|
| ProcessEnvironmentTest.cs | ⏳ Needs Port | Process execution still relevant |

### 10. ServiceTests (Service Layer)

| V2 Test File | Status | V3 Equivalent |
|-------------|--------|---------------|
| ContainerServiceBasicTests.cs | ⏳ Needs Port | Need Unit/Service/ContainerServiceTests.cs |
| DockerComposeTests.cs | ⏳ Needs Port | Need Unit/Service/ComposeServiceTests.cs |
| MachineServiceTests.cs | ❌ Deprecated | Docker Machine is deprecated |
| NetworkServiceTests.cs | ⏳ Needs Port | Need Unit/Service/NetworkServiceTests.cs |

---

## Tests to Implement (Priority Order)

### High Priority - Core Functionality

1. **Unit/Builder/ContainerBuilderTests.cs** - Test all container builder methods
2. **Unit/Builder/WaitConditionTests.cs** - Test WaitForPort, WaitForHealthy, WaitLambda
3. **Unit/Builder/LifecycleHookTests.cs** - Test CopyTo, CopyFrom, Execute hooks
4. **Unit/Service/ContainerServiceTests.cs** - Test ContainerService operations
5. **Unit/Service/NetworkServiceTests.cs** - Test NetworkService operations
6. **Unit/Service/VolumeServiceTests.cs** - Test VolumeService operations
7. **Unit/Service/ComposeServiceTests.cs** - Test ComposeService operations

### Medium Priority - Integration Tests

8. **Integration/ContainerTests.cs** - Full container lifecycle tests
9. **Integration/NetworkTests.cs** - Network create/connect tests
10. **Integration/VolumeTests.cs** - Volume create/mount tests
11. **Integration/ComposeTests.cs** - Docker Compose tests
12. **Integration/WaitTests.cs** - Wait condition integration tests

### Low Priority - Utilities

13. **Unit/Common/LoggerTests.cs** - Logger tests
14. **Unit/Parser/ResponseParserTests.cs** - CLI output parser tests
15. **Unit/Extensions/ConversionTests.cs** - Type conversion tests

---

## Summary Statistics

| Category | Total | Implemented | Planned | Deprecated | New in V3 |
|----------|-------|-------------|---------|------------|-----------|
| Command Tests | 7 | 0 | 0 | 7 | 0 |
| Common | 1 | 0 | 1 | 0 | 0 |
| Extension Tests | 4 | 1 | 2 | 1 | 0 |
| FluentApi Tests | 9 | 2 | 6 | 1 | 0 |
| Model Tests | 6 | 0 | 1 | 5 | 0 |
| Parser Tests | 1 | 0 | 1 | 0 | 0 |
| Process Tests | 1 | 0 | 1 | 0 | 0 |
| Service Tests | 4 | 0 | 3 | 1 | 0 |
| **V3 Unit Tests** | 239 | 239 | - | - | 239 |
| **Docker Driver Tests** | 10+ | 0 | 10+ | - | 10+ |
| **TOTAL** | 280+ | 241 | 25+ | 15 | 249 |

### Test Implementation Status
- ✅ **Unit Tests**: 239 tests fully implemented (all passing)
- ✅ **Integration Tests**: Basic builder and kernel tests implemented
- 📋 **Docker Driver Tests**: Structure planned, requires Docker to be running

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


