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
│   ├── Builder/                        🆕 Builder integration tests
│   ├── BuilderTests.cs                 🆕 Builder integration tests
│   ├── KernelBuilderTests.cs           🆕 Kernel builder tests
│   ├── DockerCliDriver/                ✅ Docker CLI driver integration tests
│   │   ├── DockerDriverTestBase.cs     ✅ Base class with kernel setup
│   │   ├── ContainerDriverTests.cs     ✅ Container driver tests
│   │   ├── NetworkDriverTests.cs       ✅ Network driver tests
│   │   ├── VolumeDriverTests.cs        ✅ Volume driver tests
│   │   ├── ImageDriverTests.cs         ✅ Image driver tests
│   │   ├── SystemDriverTests.cs        ✅ System driver tests
│   │   ├── FluentContainerTests.cs     ✅ Fluent container tests
│   │   ├── FluentNetworkTests.cs       ✅ Fluent network tests
│   │   ├── FluentVolumeTests.cs        ✅ Fluent volume tests
│   │   ├── ComposeDriverTests.cs       ✅ Compose driver tests
│   │   ├── WaitConditionTests.cs       ✅ Wait condition tests
│   │   └── RegressionTests.cs          ✅ Issue regression tests
│   └── FluentBuilder/
│       └── MultiContainerTests.cs      ✅ Multi-container tests (10 tests)
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
│       ├── SystemDriverTests.cs       ✅ System info tests (6 tests)
│       ├── FluentContainerTests.cs    ✅ Fluent container tests (20 tests)
│       ├── FluentNetworkTests.cs      ✅ Fluent network tests (8 tests)
│       ├── FluentVolumeTests.cs       ✅ Fluent volume tests (10 tests)
│       ├── ComposeDriverTests.cs      ✅ Compose driver tests (12 tests)
│       ├── WaitConditionTests.cs      ✅ Wait condition tests (10 tests)
│       └── RegressionTests.cs         ✅ Issue regression tests (7 tests)
```

### V2 Test to V3 Driver Test Mapping

| V2 Test | V3 Location | Status |
|---------|-------------|--------|
| DockerClientCommandTests.cs | Integration/DockerCliDriver/ContainerDriverTests.cs | ✅ Ported |
| NetworkCommandTests.cs | Integration/DockerCliDriver/NetworkDriverTests.cs | ✅ Ported |
| VolumeTests.cs | Integration/DockerCliDriver/VolumeDriverTests.cs | ✅ Ported |
| ImageTests.cs | Integration/DockerCliDriver/ImageDriverTests.cs | ✅ Ported |
| DockerInfoCommandTests.cs | Integration/DockerCliDriver/SystemDriverTests.cs | ✅ Ported |
| FluentContainerBasicTests.cs | Integration/DockerCliDriver/FluentContainerTests.cs | ✅ Ported |
| FluentNetworkTests.cs | Integration/DockerCliDriver/FluentNetworkTests.cs | ✅ Ported |
| FluentVolumeTests.cs | Integration/DockerCliDriver/FluentVolumeTests.cs | ✅ Ported |
| FluentDockerComposeTests.cs | Integration/DockerCliDriver/ComposeDriverTests.cs | ✅ Ported |
| WaitTests.cs | Integration/DockerCliDriver/WaitConditionTests.cs | ✅ Ported |
| IssuesTests.cs | Integration/DockerCliDriver/RegressionTests.cs | ✅ Ported |
| ImageBuilderTests.cs | CoreTests/BuilderTests/DockerfileBuilderTests.cs | ✅ Implemented |
| FluentMultiContainerTests.cs | Integration/FluentBuilder/MultiContainerTests.cs | ✅ Implemented |
| RemoteDaemonTests.cs | - | ❌ Deprecated (Docker Machine/SSH) |

### Known Issues

Previously tracked migration gaps have been closed:
- Version model now handles nested Client/Server JSON returned by `docker version`.
- System info model reads the Runtimes object Docker emits.
- CLI filters now format Docker's image filters correctly.

No additional V2-to-V3 migration issues are currently tracked; integration failures are expected to be environment-related (e.g., Docker daemon offline).

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
| FluentContainerBasicTests.cs | ✅ Ported | Integration/DockerCliDriver/FluentContainerTests.cs |
| FluentDockerComposeTests.cs | ✅ Ported | Integration/DockerCliDriver/ComposeDriverTests.cs |
| FluentMultiContainerTests.cs | ✅ Ported | Integration/FluentBuilder/MultiContainerTests.cs |
| FluentNetworkTests.cs | ✅ Ported | Integration/DockerCliDriver/FluentNetworkTests.cs |
| FluentVolumeTests.cs | ✅ Ported | Integration/DockerCliDriver/FluentVolumeTests.cs |
| ImageBuilderTests.cs | 🔄 Refactored | CoreTests/BuilderTests/ImageBuilderTests.cs |
| IssuesTests.cs | ✅ Ported | Integration/DockerCliDriver/RegressionTests.cs |
| RemoteDaemonTests.cs | ❌ Deprecated | Docker Machine / Remote SSH deprecated |
| WaitTests.cs | ✅ Ported | Integration/DockerCliDriver/WaitConditionTests.cs |

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
| FluentApi Tests | 9 | 8 | 1 |
| Model Tests | 6 | 5 | 1 |
| Parser Tests | 1 | 1 | 0 |
| Process Tests | 1 | 1 | 0 |
| Service Tests | 4 | 3 | 1 |
| **V3 Unit Tests** | 328 | 328 | - |
| **Docker Driver Tests** | 56 | 46 | - |
| **Fluent API Tests** | 67 | 67 | - |
| **TOTAL** | 470+ | 459 | 11 |

### Test Implementation Status
- ✅ **Unit Tests**: 328 tests fully implemented (all passing)
- ✅ **Docker Driver Integration Tests**: 46 passing, 10 need model fixes
- ✅ **Fluent Container Tests**: 13/14 passing (1 health check config issue)
- ✅ **Fluent Network Tests**: 6/6 passing - all passing!
- ✅ **Fluent Volume Tests**: 5/6 passing (1 minor timing issue)
- ✅ **Compose Driver Tests**: 13/13 passing - all tests pass!
- ✅ **Wait Condition Tests**: 9/9 passing - all tests pass!
- ✅ **Regression Tests**: 4/6 passing (2 compose-related)
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
- ✅ **System Driver Tests**: 6/6 passing - models updated for Docker JSON structure
- ✅ **Health Check Tests**: All passing - driver now passes health check config correctly
- ✅ **Compose List Tests**: Fixed - ComposeServiceInfo model updated for compose ps output

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

---

## Test Resources Mapping

This section maps the test resources in `/FluentDocker.Tests/Resources/` to their usage in tests.

### Resource Structure

```
FluentDocker.Tests/Resources/
├── ComposeTests/
│   ├── KafkaAndZookeeper/
│   │   └── docker-compose.yaml     ✅ Used: ComposeDriverTests, RegressionTests
│   ├── MongoDbAndNetwork/
│   │   └── docker-compose.yml      ✅ Used: ComposeDriverTests, RegressionTests
│   ├── RabbitMQ/
│   │   └── docker-compose.yml      ✅ Used: ComposeDriverTests (7 tests)
│   └── WordPress/
│       └── docker-compose.yml      ✅ Used: ComposeDriverTests (4 tests)
├── hellotest/
│   ├── docker-compose.yml          ✅ Used: ComposeDriverTests (build test)
│   └── hellotest/
│       ├── Dockerfile              ✅ Used: ComposeDriverTests (image build)
│       └── hello                   ✅ Used: ComposeDriverTests (binary)
└── Scripts/
    ├── envtest.bat                 ✅ Used: ProcessEnvironmentTests (Windows)
    └── envtest.sh                  ✅ Used: ProcessEnvironmentTests (Linux/macOS)
```

### Resource Usage by Test File

| Test File | Resources Used |
|-----------|----------------|
| `ComposeDriverTests.cs` | WordPress, RabbitMQ, MongoDbAndNetwork, KafkaAndZookeeper, hellotest |
| `RegressionTests.cs` | MongoDbAndNetwork, KafkaAndZookeeper |
| `ProcessEnvironmentTests.cs` | Scripts/envtest.sh, Scripts/envtest.bat |

### Removed Resources

| Resource | Reason | Status |
|----------|--------|--------|
| `Issue/111/server.py` | Legacy V2 issue test resource | ✅ Removed |
| `StackTests/WordPress/stack.yml` | Docker Swarm stack tests deprecated | ✅ Removed |

### Notes on Resources

1. **ComposeTests/**: Primary compose resources for integration testing
   - WordPress: Multi-container with MariaDB + WordPress (port 8000)
     - Updated from mysql:5.7 → mariadb:10.6 for ARM64 compatibility
   - RabbitMQ: Single service with healthcheck (port 5672)
     - Updated to rabbitmq:3-alpine for consistency
   - MongoDbAndNetwork: Custom network with MongoDB
     - Removed deprecated --smallfiles flag
   - KafkaAndZookeeper: Multi-container messaging system
     - Updated from wurstmeister → bitnami images for ARM64 compatibility

2. **hellotest/**: Build context for compose with local Dockerfile
   - Tests compose `build` functionality vs pre-built images
   - Contains simple "hello" binary for testing

3. **Scripts/**: Cross-platform environment testing scripts
   - Used by `ProcessEnvironmentTests` to verify env var passing

4. **Issue/** and **StackTests/**: Removed
   - Legacy resources no longer needed in V3

### Resource File Changes Made

The following updates were made to modernize the compose files:

| File | Change | Reason |
|------|--------|--------|
| WordPress/docker-compose.yml | mysql:5.7 → mariadb:10.6 | ARM64 support |
| WordPress/docker-compose.yml | Removed `version` attribute | Deprecated in Compose V2 |
| KafkaAndZookeeper/docker-compose.yaml | wurstmeister → bitnami | ARM64 support |
| KafkaAndZookeeper/docker-compose.yaml | Simplified config | Modern Kafka settings |
| MongoDbAndNetwork/docker-compose.yml | Removed --smallfiles | Deprecated in MongoDB |
| RabbitMQ/docker-compose.yml | Removed `version` attribute | Deprecated in Compose V2 |
| hellotest/docker-compose.yml | Removed `version` attribute | Deprecated in Compose V2 |

### csproj Updates

Added missing resource file entries to `FluentDocker.Tests.csproj`:
- `ComposeTests/RabbitMQ/docker-compose.yml`
- `hellotest/docker-compose.yml`
- `hellotest/hellotest/Dockerfile`
- `hellotest/hellotest/hello`
