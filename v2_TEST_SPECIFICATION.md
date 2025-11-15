# FluentDocker v2.x.x Test Specification

**Date:** November 15, 2025
**Total Test Count:** 163 test methods across 33 test files
**Test Framework:** Microsoft.VisualStudio.TestTools.UnitTesting (MSTest)

---

## Executive Summary

This document provides a comprehensive analysis of all v2.x.x tests in the FluentDocker.Tests project (excluding V3 folder). The test suite is organized into 6 primary categories plus a Model category, covering command execution, Fluent API usage, service management, extension utilities, and data model validation.

---

## Test Categories Overview

| Category | Files | Tests | Type | Purpose |
|----------|-------|-------|------|---------|
| **CommandTests** | 7 | 32 | Integration | Low-level Docker CLI command execution |
| **ExtensionTests** | 3 | 17 | Unit | Extension method validation |
| **FluentApiTests** | 9 | 55 | Integration | High-level Fluent API usage patterns |
| **ServiceTests** | 4 | 36 | Integration | Docker service layer operations |
| **ProcessTests** | 1 | 1 | Unit | Process environment and execution |
| **ProcessResponseParsersTests** | 1 | 2 | Unit | Response parsing validation |
| **Model** | 8 | 20 | Unit | Data model and builder validation |
| **TOTAL** | **33** | **163** | | |

---

## 1. CommandTests Category

**Files:** 7
**Test Methods:** 32
**Location:** `/home/user/FluentDocker/Ductus.FluentDocker.Tests/CommandTests/`

### Overview
Tests for low-level Docker command execution and response parsing. These are integration tests that require a Docker daemon running and validate raw command output from Docker CLI.

### Test Files and Patterns

#### 1.1 ClientStreamCommandTests.cs (4 tests)
**Focus:** Event streaming and log streaming from Docker daemon

| Test Name | Type | Description |
|-----------|------|-------------|
| `StartEventShallBeEmittedWhenContainerStart` | Integration | Verifies that container start events are correctly emitted through event stream |
| `LogsFromContainerWhenNotFollowModeShallExitByItself` | Integration | Validates that log stream exits automatically in non-follow mode |
| `LogsFromContainerWhenInFollowModeShallExitWhenCancelled` | Integration | Tests that follow mode log stream can be cancelled |
| `LogFromContainerShouldSupportReadAllExtension` | Integration | Validates ReadToEnd extension on log streams |

**Pattern:** Uses ClassInitialize to setup test machine infrastructure. Tests stream-based operations with cleanup in finally blocks. Uses CertificatePaths for TLS connections to Docker machines.

#### 1.2 ComposeCommandTests.cs (4 tests)
**Focus:** Docker Compose command execution and orchestration

| Test Name | Type | Description |
|-----------|------|-------------|
| `ComposeIsEitherSeparateOrSubCommand` | Unit | Validates Docker Compose version detection (v1 vs v2) |
| `ComposeByBuildImageAddNginxAsLoadBalancerTwoNodesAsHtmlServeAndRedisAsDbBackendShouldWorkAsCluster` | Integration | Complex multi-container compose test with load balancer setup |
| `Issue79_DockerComposeOnDockerMachineShallWork` | Integration | Regression test for Docker Machine compose support |
| `WaitFlagAndWaitTimeoutWorks` | Integration | Tests compose wait flags and timeout behavior |

**Pattern:** Uses DockerHost/TestBase for environment setup. Tests async HTTP operations. Resource extraction from embedded test files.

#### 1.3 DockerClientCommandTests.cs (9 tests)
**Focus:** Core Docker container operations at command level

| Test Name | Type | Description |
|-----------|------|-------------|
| `EnsureLinuxDaemonShallWork` | Integration | Validates switching to Linux daemon mode |
| `EnsureWindowsDaemonShallWork` | Integration | Tests Windows daemon mode (requires Windows host) |
| `RunWithoutArgumentShallSucceed` | Integration | Basic container creation without parameters |
| `RemoveContainerShallSucceed` | Integration | Container deletion verification |
| `DockerPsWithOneContainerShallGiveOneResult` | Integration | Container listing and filtering |
| `InspectRunningContainerShallSucceed` | Integration | Container inspection with state validation |
| `InspectContainersShallSucceed` | Integration | Multiple container inspection |
| `InspectContainersByIdsShallSucceed` | Integration | Filtered container inspection by IDs |
| `DiffContainerShallWork` | Integration | Container filesystem changes detection |
| `RunPostgresContainerAndCheckThatAllProcessesAreRunning` | Integration | Process listing inside container |

**Pattern:** Each test follows a try-finally pattern for cleanup. Tests verify exact response values (64-char IDs, specific process names). Uses multiple docker commands in sequence.

#### 1.4 DockerInfoCommandTests.cs (1 test)
**Focus:** Docker daemon metadata

| Test Name | Type | Description |
|-----------|------|-------------|
| `GetServerClientVersionInfoShallSucceed` | Integration | Docker version and server info retrieval |

**Pattern:** Simple assertion-based test. Prints debug output.

#### 1.5 DockerMachineCommandTests.cs (7 tests)
**Focus:** Docker Machine lifecycle operations (deprecated but still tested)

| Test Name | Type | Description |
|-----------|------|-------------|
| `ManuallyForceDeleteMachine` | Integration | Force deletion of Docker machine |
| `InspectDockerMachine` | Integration | Machine metadata inspection |
| `CreateDockerMachineShallSucceed` | Integration | Machine creation and lifecycle |
| `MachineLsWhenRunningContainerShallReturnStateAndValidUrl` | Integration | Machine discovery and state |
| `CreateAndStartStopDockerMachineShallSucceed` | Integration | Machine start/stop lifecycle |
| `CreateAndStartStopDockerMachineShallGiveSaneEnvironment` | Integration | Machine environment variable setup |

**Pattern:** All tests marked with `[Ignore]` - legacy tests for Docker Machine which is deprecated. Tests create/delete test machines with cleanup.

#### 1.6 ImageTests.cs (2 tests)
**Focus:** Docker image operations

| Test Name | Type | Description |
|-----------|------|-------------|
| `ImageConfigurationShallBeRetrievable` | Integration | Image config/metadata retrieval |
| `ImageIsExposedOnARunningContainer` | Integration | Container image property access |

**Pattern:** Uses ClassInitialize to set Linux mode. Validates image configuration objects.

#### 1.7 NetworkCommandTests.cs (6 tests)
**Focus:** Docker network operations

| Test Name | Type | Description |
|-----------|------|-------------|
| `NetworkDiscoverShallWork` | Integration | Network listing and filtering |
| `NetworkInspectShallWork` | Integration | Network configuration inspection |
| `NetworkCreateAndDeleteShallWork` | Integration | Network lifecycle operations |
| `UseNetworkAndStaticIpv4ShallWork` | Integration | Custom network with static IP assignment |
| `ConnectAndDisconnectContainerToNetworkShallWork` | Integration | Dynamic network attachment |

**Pattern:** Complex setup with certificate paths and machine initialization. Tests both standalone networks and containers with networks.

### CommandTests Key Patterns

- **Setup Pattern:** ClassInitialize checks for native Docker or falls back to Docker Machine with certificate setup
- **Cleanup Pattern:** Try-finally blocks with RemoveContainer/Delete calls
- **Assertion Style:** Mix of Assert.IsTrue/IsFalse and Assert.AreEqual with static imports
- **Test Data:** Uses hardcoded image names (nginx:1.13.6-alpine, postgres:9.6-alpine)
- **Integration Level:** All require actual Docker daemon, container creation, and removal

---

## 2. ExtensionTests Category

**Files:** 3
**Test Methods:** 17
**Location:** `/home/user/FluentDocker/Ductus.FluentDocker.Tests/ExtensionTests/`

### Overview
Unit tests for extension methods on strings and other types. These tests are lightweight and don't require Docker daemon.

### Test Files and Patterns

#### 2.1 CommandExtensionTest.cs (1 test)
**Focus:** Binary resolution and exception handling

| Test Name | Type | Description |
|-----------|------|-------------|
| `MissingDockerComposeShallThrowExceptionInResolveBinary` | Unit | Validates FluentDockerException when docker-compose is missing |

**Pattern:** Creates fake docker binary, tests exception. Uses ExpectedException attribute.

#### 2.2 ConversionExtensionTests.cs (8 tests)
**Focus:** String-to-numeric conversion with size units

| Test Name | Type | Description |
|-----------|------|-------------|
| `NullStringShallGiveMinimumValue` | Unit | Null/empty string handling |
| `InvalidUnitInputShallGiveMinimumValue` | Unit | Invalid format detection |
| `LessThanLongMinimumValueShallGiveMinimumValue` | Unit | Overflow handling |
| `GreaterThanLongMaximumValueShallGiveMinimumValue` | Unit | Underflow handling |
| `ValidByteInputShallGiveExactNumber` | Unit | Byte unit parsing (42b = 42) |
| `ValidKilobyteInputShallGiveCorrectKilobyteNumber` | Unit | Kilobyte parsing (42k = 43008) |
| `ValidMegabyteInputShallGiveCorrectMegabyteNumber` | Unit | Megabyte parsing (42m = 44040192) |
| `ValidGigabyteInputShallGiveCorrectGigabyteNumber` | Unit | Gigabyte parsing (42g = 45097156608) |

**Pattern:** DataRow attributes for parameterized testing. Tests conversion of Docker memory limit strings to bytes.

#### 2.3 EnvironmentExtensionTests.cs (8 tests)
**Focus:** Environment variable string parsing

| Test Name | Type | Description |
|-----------|------|-------------|
| `NullStringShallGiveNullReturnInExtract` | Unit | Null input handling |
| `EmptyStringShallGiveNullReturnInExtract` | Unit | Empty string handling |
| `OnlyWhitespaceStringShallGiveNullReturnInExtract` | Unit | Whitespace-only handling |
| `SingleNameNotEqualSignGivesStringShallGiveNameAnEmptyStringReturnInExtract` | Unit | Variable name without value |
| `SingleNameWithEqualSignGivesStringShallGiveNameAnEmptyStringReturnInExtract` | Unit | Variable name with = but no value |
| `NameValueShallReturnNameAndValue` | Unit | Basic NAME=value parsing |
| `ItShallBePossibleToHaveEqualSignsInTheValue` | Unit | Values containing = characters |

**Pattern:** Returns Tuple<string, string> for name and value. Tests edge cases in environment variable parsing.

#### 2.4 ResourceExtensionsTests.cs (2 tests - named ResourceExtensionTests in error)
**Focus:** Embedded resource discovery

| Test Name | Type | Description |
|-----------|------|-------------|
| `QueryResourcesRecusivelyShallWork` | Unit | Recursive resource discovery with filtering |
| `QueryResourcesNonRecurivelyShallWork` | Unit | Non-recursive resource listing |

**Pattern:** Uses reflection to query assembly resources. Tests Docker Compose test files and Dockerfiles.

### ExtensionTests Key Patterns

- **Assertion Style:** Assert.AreEqual, Assert.IsTrue/IsFalse, Assert.IsNull
- **Parameterization:** DataRow attributes for multiple test cases
- **Exception Testing:** ExpectedException attribute
- **No Docker Dependency:** All tests are fast unit tests
- **Edge Case Focus:** Null, empty, invalid input handling

---

## 3. FluentApiTests Category

**Files:** 9
**Test Methods:** 55
**Location:** `/home/user/FluentDocker/Ductus.FluentDocker.Tests/FluentApiTests/`

### Overview
Tests for the high-level Fluent API that provides builder-style configuration. These are comprehensive integration tests covering containers, images, networks, and volumes.

### Test Files and Patterns

#### 3.1 FluentContainerBasicTests.cs (19 tests)
**Focus:** Container lifecycle and configuration using Fluent API

| Test Name | Type | Description |
|-----------|------|-------------|
| `BuildContainerRenderServiceInStoppedMode` | Integration | Container created in stopped state |
| `UseStaticBuilderWillAlwaysRunDisposeOnContainer` | Integration | Static builder with automatic cleanup |
| `UseStaticBuilderAsExtension` | Integration | Extension method on builder |
| `BuildAndStartContainerWithKeepContainerWillLeaveContainerInArchive` | Integration | KeepContainer flag for preservation |
| `BuildAndStartContainerWithCustomEnvironmentWillBeReflectedInGetConfiguration` | Integration | Environment variable propagation |
| `PauseAndResumeShallWorkOnSingleContainer` | Integration | Container pause/resume lifecycle |
| `ExplicitPortMappingShouldWork` | Integration | Explicit port mapping (host:container) |
| `ImplicitPortMappingShouldWork` | Integration | Random host port assignment |
| `FullImplicitPortMappingShouldWork` | Integration | ExposeAllPorts functionality |
| `ExposeAllPortsIsMutuallyExclusiveWithExposePort` | Integration | Mutual exclusivity validation |
| `ExposePortIsMutuallyExclusiveWithExposeAllPorts` | Integration | Reverse mutual exclusivity check |
| `WaitForPortShallWork` | Integration | Port availability waiting |
| `WaitForProcessShallWork` | Integration | Process startup waiting |
| `VolumeMappingShallWork` | Integration | Read-only volume mounting with file I/O |
| `VolumeMappingWithSpacesShallWork` | Integration | Volume paths with spaces |
| `CopyFromRunningContainerShallWork` | Integration | Container-to-host file copying |
| `CopyBeforeDisposeContainerShallWork` | Integration | CopyOnDispose functionality |
| `ExportToTarFileWhenDisposeShallWork` | Integration | Container export to tar on dispose |
| `ExportExplodedWhenDisposeShallWork` | Integration | Container export as directory tree |

**Pattern:** Uses Fd static factory methods. Tests fluent builder chaining. Extensive use of using() for resource cleanup. Validates API design with assertion of exceptions on mutually exclusive operations.

#### 3.2 FluentDockerComposeTests.cs (11 tests)
**Focus:** Docker Compose orchestration with Fluent API

| Test Name | Type | Description |
|-----------|------|-------------|
| `WordPressDockerComposeServiceShallShowInstallScreen` | Integration | Multi-container WordPress setup with HTTP validation |
| `KeepContainersShallWorkForCompositeServices` | Integration | KeepContainer flag on composite services |
| `KeepRunningShallWorkForCompositeServices` | Integration | KeepRunning flag on composite services |
| `DockerComposePauseResumeShallWork` | Integration | Composite service pause/resume |
| `ComposeWaitForHttpShallWork` | Integration | HTTP-based waiting with continuation callback |
| `ComposeWaitForHttpThatFailShallBeAborted` | Integration | Exception on HTTP wait timeout |
| `ComposeWaitForCustomLambdaShallWork` | Integration | Custom lambda-based waiting |
| `ComposeWaitForHealthyShallWork` | Integration | Health check waiting with RabbitMQ |
| `ComposeRunOnRemoteMachineShallWork` | Integration | Remote SSH Docker daemon execution |
| `Issue85` | Integration | MongoDB network configuration |
| `Issue94` | Integration | Kafka/Zookeeper service naming |

**Pattern:** Uses resource files (docker-compose.yml files). Tests both file-based and programmatic setup. Async HTTP operations. Complex cleanup with multiple containers.

#### 3.3 FluentMultiContainerTests.cs (1 test - IGNORED)
**Focus:** Custom image building with multi-container orchestration

| Test Name | Type | Description |
|-----------|------|-------------|
| `DefineAndBuildImageAddNginxAsLoadBalancerTwoNodesAsHtmlServeAndRedisAsDbBackendShouldWorkAsCluster` | Integration (Ignored) | Complex image build with node.js, nginx load balancer |

**Pattern:** Marked as Ignore. Would test DefineImage API with embedding resources.

#### 3.4 FluentNetworkTests.cs (3 tests)
**Focus:** Custom network configuration with Fluent API

| Test Name | Type | Description |
|-----------|------|-------------|
| `StaticIpv4InCustomNetworkShallWork` | Integration | Custom network with static IPv4 assignment |
| `InternalNetworkExposedToHostShallWork` | Integration | Internal network isolation testing |
| `CustomResolverForContainerShallWork` | Integration | Custom port resolution callback |

**Pattern:** Creates custom networks with UseNetwork(). Tests network isolation. Custom resolver validation with ExecutedCustomResolver flag.

#### 3.5 FluentVolumeTests.cs (3 tests)
**Focus:** Volume lifecycle with Fluent API

| Test Name | Type | Description |
|-----------|------|-------------|
| `VolumeShallNotDeletedWhenRemoveOnDisposeIsNotPresent` | Integration | Volume persistence without RemoveOnDispose |
| `VolumeShallBeDeletedWhenRemoveOnDispose` | Integration | Volume deletion on disposal |
| `VolumeShallBeUsedWhenMounted` | Integration | Volume mounting to container |

**Pattern:** Tests named volumes with Fd.UseVolume(). Validates RemoveOnDispose flag behavior.

#### 3.6 ImageBuilderTests.cs (4 tests)
**Focus:** Dockerfile generation and image building

| Test Name | Type | Description |
|-----------|------|-------------|
| `BuildImageShallPreserveLineOrdering` | Unit | Dockerfile instruction ordering |
| `BuildImageFromFileWithCopyAndRunInstructionShallWork` | Integration (Ignored) | Image build with copy and run |
| `BuildImageShouldPropagateBuildArguments` | Integration (Ignored) | Build argument propagation |
| `URLInCopyShallWork` | Unit | URL handling in COPY instructions |

**Pattern:** Tests Dockerfile builder API. Validates instruction formatting and line ordering.

#### 3.7 IssuesTests.cs (1 test)
**Focus:** Regression tests for specific issues

| Test Name | Type | Description |
|-----------|------|-------------|
| `Issue111_WaitForProcess` | Integration | Windows container process waiting |

**Pattern:** Tests Windows-specific features (DefineImage with Windows base). Engine scope testing with EngineScopeType.Windows.

#### 3.8 RemoteDaemonTests.cs (2 tests - IGNORED)
**Focus:** Remote Docker daemon via SSH

| Test Name | Type | Description |
|-----------|------|-------------|
| `CreateSshConnectionToRemoteDockerAndCreateContainerShallWork` | Integration (Ignored) | SSH-based remote daemon connection |
| `UseNamedDockerMachineForRemoteSshDaemonConnectionShallWork` | Integration (Ignored) | Machine-based SSH daemon |

**Pattern:** Tests UseSsh() builder API. Requires valid SSH key paths and remote host.

#### 3.9 WaitTests.cs (4 tests)
**Focus:** Container waiting and callback mechanisms

| Test Name | Type | Description |
|-----------|------|-------------|
| `SingleWaitLambdaShallGetInvoked` | Integration | Custom wait callback invocation |
| `WaitLambdaWithReusedContainerShallGetInvoked` | Integration | Wait callback with ReuseIfExists |
| `Issue92` | Integration | MailHog service HTTP waiting |

**Pattern:** Tests custom lambda-based waiting with database connection checks. Uses Npgsql for PostgreSQL connection testing.

### FluentApiTests Key Patterns

- **Builder Style:** Fluent API with method chaining (Fd.UseContainer().UseImage()...)
- **Resource Management:** Heavy use of using() statements for disposal
- **Async Operations:** Async HTTP calls with Wget() extension
- **Wait Mechanisms:** Port waiting, process waiting, HTTP waiting, custom lambdas
- **Test Data:** Hardcoded image names, uses live external services
- **Exception Testing:** Tests mutual exclusivity of API options
- **File I/O:** Creates temporary directories with TemplateString

---

## 4. ServiceTests Category

**Files:** 4
**Test Methods:** 36
**Location:** `/home/user/FluentDocker/Ductus.FluentDocker.Tests/ServiceTests/`

### Overview
Tests for the service layer that provides abstractions over Docker operations. These are integration tests focusing on IContainerService, INetworkService, and ICompositeService interfaces.

### Test Files and Patterns

#### 4.1 ContainerServiceBasicTests.cs (26 tests)
**Focus:** IContainerService operations and container lifecycle

| Test Name | Type | Description |
|-----------|------|-------------|
| `CreateContainerMakesServiceStopped` | Integration | Initial container state is stopped |
| `Issue_69_Container_name_lost_first_char` | Integration | Container name with underscore prefix |
| `CreateAndStartContainerWithEnvironment` | Integration | Environment variable setting |
| `DeleteVolumesOnContainerDisposeShallWork` | Integration | Volume deletion on dispose |
| `DeleteNamedVolumesOnContainerDisposeShallWork` | Integration | Named volume deletion |
| `ListVolumesShallWork` | Integration | Volume inspection |
| `CreateAndStartContainerWithOneExposedPortVerified` | Integration | Port exposure and waiting |
| `ProcessesInContainerAndManuallyVerifyPostgresIsRunning` | Integration | Process listing in container |
| `ExportRunningContainerToTarFileShallSucceed` | Integration | Container export to tar |
| `ExportRunningContainerExplodedShallSucceed` | Integration | Container export as directory |
| `UseHostVolumeInsideContainerWhenMountedShallSucceed` | Integration | Host volume mounting with HTTP test |
| `CopyFromRunningContainerShallWork` | Integration | File copying from container |
| `CopyToRunningContainerShallWork` | Integration | File copying to container with Diff |
| `WaitingForSpecificStatesShallWork` | Integration | State waiting (running, stopped) |
| `GetContainersShallWork` | Integration | Container listing and serialization |
| `GetContainersShallWorkWithFilterWhenThereIsNoResults` | Integration | Container filtering with no results |
| `GetContainersShallWorkWithFilterWhenThereIsResults` | Integration | Container filtering with results |

**Pattern:** ClassInitialize discovers or creates host. Uses IHostService abstraction. Tests both CLI-based operations and service layer.

#### 4.2 DockerComposeTests.cs (4 tests)
**Focus:** ICompositeService and DockerComposeCompositeService

| Test Name | Type | Description |
|-----------|------|-------------|
| `WordPressDockerComposeServiceShallShowInstallScreen` | Integration | Composite service with HTTP verification |
| `StartErrorMessageShallContainDockerComposeFilePaths` | Unit | Error message validation for non-existent files |
| `DisposeErrorMessageShallContainDockerComposeFilePaths` | Unit | Dispose error message validation |
| `CompositeBuilderBuildErrorNoHostFoundShallContainComposeFilePaths` | Unit | Builder error message validation |

**Pattern:** Tests both happy path and error scenarios. Error messages include file paths for debugging.

#### 4.3 MachineServiceTests.cs (5 tests)
**Focus:** Docker Machine discovery and host management

| Test Name | Type | Description |
|-----------|------|-------------|
| `DiscoverBinariesShallWork` | Integration | Docker binary resolution |
| `DiscoverShouldReturnNativeWhenSuchIsPresent` | Integration | Native Docker discovery |
| `DiscoverShallReturnMachines` | Integration (Ignored) | Docker Machine discovery |
| `CreateHostFromNonExistingMachineRegistryEntryShallThrowExceptionWhenThrowIfNotStarted` | Unit | Exception on missing machine |
| `CreateHostFromExistingMachineRegistryEntryShallWork` | Integration (Ignored) | Existing machine lookup |

**Pattern:** Conditional execution based on platform (native vs machine-based). Tests both native and machine setups.

#### 4.4 NetworkServiceTests.cs (13 tests)
**Focus:** INetworkService operations and network management

| Test Name | Type | Description |
|-----------|------|-------------|
| `DiscoverNetworksShallWork` | Integration | Network listing and filtering |
| `NetworkIsDeletedWhenDisposedAndFlagIsSet` | Integration | Network removal on dispose |
| `AttachWithAliasShallWorkWithIContainerService` | Integration | Network attachment with alias |
| `AttachWithAliasShallWorkWithContainerId` | Integration | Network attachment by container ID |
| `UseNetworkWithAliasShallWorkWithINetworkService` | Integration | UseNetworksWithAlias API |
| `UseNetworkWithAliasShallWorkWithString` | Integration | Network by name with alias |
| `MultipleUseNetworkWithAliasShallWorkWithINetworkService` | Integration | Multiple networks with different aliases |
| `MultipleUseNetworkWithAliasShallWorkWithString` | Integration | Multiple networks by name |
| `MultipleUseNetworkWithSameAliasShallWorkWithINetworkService` | Integration | Multiple networks with same alias |
| `MultipleUseNetworkWithSameAliasShallWorkWithString` | Integration | Multiple networks by name with same alias |

**Pattern:** Tests complex network scenarios with aliases. Uses Alpine image for curl-based network testing. Validates Docker DNS resolution within networks.

### ServiceTests Key Patterns

- **Service Abstraction:** Tests interface-based abstractions (IHostService, IContainerService, etc.)
- **Host Discovery:** Automatic platform detection and host initialization
- **Error Validation:** Error messages include diagnostic information
- **Reflection:** Uses reflection to access internal builders
- **JSON Serialization:** Uses Newtonsoft.Json for container comparison
- **State Management:** Tests container/network state transitions

---

## 5. ProcessResponseParsersTests Category

**Files:** 1
**Test Methods:** 2
**Location:** `/home/user/FluentDocker/Ductus.FluentDocker.Tests/ProcessResponseParsersTests/`

### Overview
Unit tests for Docker command response parsing. These tests validate that response strings are correctly parsed into model objects.

### Test Files and Patterns

#### 5.1 NetworkLsResponseParserTests.cs (2 tests)
**Focus:** Network listing response parsing

| Test Name | Type | Description |
|-----------|------|-------------|
| `ProcessShallParseResponse` | Unit | Standard response parsing with UTC timezone |
| `ProcessShallParseResponseWithNegativeTimezone` | Unit | Response with negative timezone offset |

**Pattern:** Uses Activator.CreateInstance for private ProcessExecutionResult constructor. Tests NetworkLsResponseParser with various date/time formats and timezones.

### ProcessResponseParsersTests Key Patterns

- **Parser Testing:** Each test creates processor result, passes to parser, validates output
- **Reflection:** Uses BindingFlags.NonPublic to instantiate internal types
- **Date/Time:** Tests timezone handling in parsed dates

---

## 6. ProcessTests Category

**Files:** 1
**Test Methods:** 1
**Location:** `/home/user/FluentDocker/Ductus.FluentDocker.Tests/ProcessTests/`

### Overview
Tests for process execution and environment variable handling.

### Test Files and Patterns

#### 6.1 ProcessEnvironmentTest.cs (1 test)
**Focus:** Custom environment variable passing to processes

| Test Name | Type | Description |
|-----------|------|-------------|
| `ProcessShallPassCustomEnvironment` | Integration | Custom environment variables in process execution |

**Pattern:** Creates ProcessExecutor, sets Env dictionary, executes external script, validates output. Tests both Windows (*.bat) and Unix (*.sh) scripts.

### ProcessTests Key Patterns

- **Cross-Platform:** Tests both Windows and Unix execution paths
- **External Processes:** Invokes external scripts for environment testing

---

## 7. Model Category

**Files:** 8
**Test Methods:** 20
**Location:** `/home/user/FluentDocker/Ductus.FluentDocker.Tests/Model/`

### Overview
Unit tests for data models and builders. These tests are fast and don't require Docker daemon.

### Test Files and Patterns

#### 7.1 Builders/CmdCommandTests.cs (4 tests)
**Focus:** CMD instruction builder for Dockerfiles

| Test Name | Type | Description |
|-----------|------|-------------|
| `SimpleConstructor` | Unit | CMD with command and arguments |
| `ToStringWithParams` | Unit | String rendering with parameters |
| `ConstructorNoParams` | Unit | CMD with no arguments |
| `ToStringNoParams` | Unit | String rendering without parameters |

**Pattern:** Simple constructor and ToString validation.

#### 7.2 Builders/ContainerBuilderConfigTests.cs (1 test - with DataTestMethod)
**Focus:** Network configuration in container builder

| Test Name | Type | Description |
|-----------|------|-------------|
| `FindFirstNetworkNameAndAlias` | Unit | Network selection logic with multiple DataRows |

**Pattern:** Extensive DataTestMethod with 30+ DataRow attributes for parametric testing of network priority logic. Uses Moq for mock INetworkService.

#### 7.3 Builders/FileBuilder/FileBuilderTest.cs (4 tests)
**Focus:** Dockerfile instruction builders

| Test Name | Type | Description |
|-----------|------|-------------|
| `FromWithAliasShallRenderUsingAS` | Unit | FROM AS multi-stage syntax |
| `ShellShallHaveCommandAndArgsSeparated` | Unit | SHELL instruction with args |
| `ShellShallSingleCommandNoArgument` | Unit | SHELL with single command |
| `ShellShallSingleCommandOneArgument` | Unit | SHELL with command and arg |

**Pattern:** Tests instruction formatting and rendering.

#### 7.4 Builders/FileBuilder/CopyCommandTests.cs (3 tests)
**Focus:** COPY instruction builder

| Test Name | Type | Description |
|-----------|------|-------------|
| `CopyCommandShallDoubleQuoteWrapAllArguments` | Unit | Quote wrapping in COPY |
| `CopyCommandShallNotAddDoubleQuoteWrapForArgumentsWithDoubleQuote` | Unit | Already-quoted argument handling |
| `CopyCommandShallEnsureBothSidesAreDoubleQuotedEvenIfArgumentHasOnlyOneSide` | Unit | Partial quote handling |

**Pattern:** Tests quote normalization in COPY instruction arguments.

#### 7.5 Common/TemplateStringTests.cs (8 tests)
**Focus:** Template string variable substitution

| Test Name | Type | Description |
|-----------|------|-------------|
| `UrlAtTheBeginningOfStringShallNotBeAlteredOnWindows` | Unit | URL preservation at string start |
| `UrlAtTheEndOfStringShallNotBeAlteredOnWindows` | Unit | URL preservation at string end |
| `UrlWithinAPowershellExpressionOnWindowsShallLeaveUrlUntouched` | Unit | URL in PowerShell script |
| `IsIsPossibleToHaveSeveralUrlsInSameStringNotAffectingWindowsPathSubstitution` | Unit | Multiple URLs with paths |
| `UnifiedSeparatorWillBeTranslatedOnWindows` | Unit | Forward slash to backslash conversion |
| `SpacesInTemplateStringIsEscaped` | Unit | Path quoting for spaces |
| `NoSpacesInTemplateStringIsNotEscaped` | Unit | No quoting without spaces |

**Pattern:** Tests cross-platform path substitution (${TEMP}, ${RND}). URL-aware to avoid false conversions of URLs. Platform-specific assertions (IsWindows checks).

#### 7.6 Containers/ContainerCreateParamsTests.cs (3 tests)
**Focus:** Container creation parameter validation

| Test Name | Type | Description |
|-----------|------|-------------|
| `NvidiaRuntimeGeneratesRuntimeOption` | Unit | Nvidia runtime flag generation |
| `ContainerIsolationGeneratesRuntimeOption` | Unit | Isolation mode flag generation (hyperv/process/default) |

**Pattern:** Tests with DataTestMethod for parameter variants.

#### 7.7 Containers/ContainerTests.cs (1 test)
**Focus:** Container model JSON deserialization

| Test Name | Type | Description |
|-----------|------|-------------|
| `TestWithNoCreated` | Unit | Container creation date edge case |

**Pattern:** Loads JSON from file, deserializes to Container model, validates edge case handling.

### Model Category Key Patterns

- **Builder Testing:** Tests instruction formatting and parameter handling
- **JSON Deserialization:** Tests model classes can handle edge cases in API responses
- **String Manipulation:** Path and URL handling tests
- **Parameterized Tests:** Heavy use of DataTestMethod/DataRow
- **No Docker Required:** All unit tests
- **Edge Case Focus:** Quote handling, URL preservation, timezone handling

---

## Testing Patterns Summary

### Integration vs Unit Test Distribution

| Category | Integration | Unit | Ignored |
|----------|-------------|------|---------|
| CommandTests | 32 | 0 | 7* |
| ExtensionTests | 0 | 17 | 0 |
| FluentApiTests | 48 | 7 | 3 |
| ServiceTests | 27 | 9 | 2 |
| ProcessResponseParsersTests | 0 | 2 | 0 |
| ProcessTests | 1 | 0 | 0 |
| Model | 0 | 20 | 0 |
| **TOTAL** | **108** | **55** | **12*** |

*Note: DockerMachineCommandTests has all tests marked [Ignore] as Docker Machine is deprecated

### Common Testing Patterns

#### 1. **Resource Cleanup Pattern**
```csharp
string id = null;
try {
    var cmd = _docker.Run("image", params);
    id = cmd.Data;
    // assertions
} finally {
    if (null != id) {
        _docker.RemoveContainer(id, true, true);
    }
}
```

#### 2. **Class Initialization Pattern**
```csharp
[ClassInitialize]
public static void Initialize(TestContext ctx) {
    if (CommandExtensions.IsNative() || CommandExtensions.IsEmulatedNative()) {
        _docker.LinuxMode();
        return;
    }
    // Fallback to Docker Machine setup
    var machine = "test-machine".Create(1024, 20000, 1);
    machine.Start();
}
```

#### 3. **Using Block Pattern** (Modern)
```csharp
using (var container = Fd.UseContainer()
    .UseImage("image")
    .Build()
    .Start()) {
    // assertions
}
```

#### 4. **Fluent Builder Pattern**
```csharp
Fd.UseContainer()
    .UseImage("postgres:9.6-alpine")
    .WithEnvironment("VAR=value")
    .ExposePort(5432)
    .WaitForPort("5432/tcp", 30000)
    .Build()
    .Start()
```

#### 5. **Async HTTP Testing Pattern**
```csharp
var response = await "http://localhost:8000/".Wget();
Assert.IsTrue(response.Contains("expected string"));
```

#### 6. **Wait/Polling Pattern**
```csharp
endpoint.WaitForPort(10000 /*10s*/);
// or
container.WaitForPort("5432/tcp", TimeSpan.FromSeconds(30));
// or custom
.Wait("service", (service, count) => CustomWaitLogic(count, service));
```

### Mocking and Isolation Patterns

- **Moq Usage:** Primarily in Model tests for INetworkService mocking
- **No Database Mocking:** All tests use real Docker containers
- **Process Execution:** Tests use real docker and docker-compose commands
- **File System:** Tests create actual temporary directories and files

### Test Data and Fixtures

- **Image Names:** Hardcoded (postgres:9.6-alpine, nginx:1.13.6-alpine, alpine, ubuntu)
- **Docker Compose Files:** Extracted from embedded resources or loaded from Resources/ directory
- **Temporary Paths:** Generated using TemplateString with ${TEMP} and ${RND}
- **SSH Keys:** Tests reference hardcoded paths (requires actual keys for remote tests)

### Error Handling and Assertions

- **Assert Methods Used:**
  - Assert.IsTrue / IsFalse
  - Assert.AreEqual / AreNotEqual
  - Assert.IsNull / IsNotNull
  - Assert.ThrowsException<T>
  - Assert.Fail for explicit failures

- **Exception Types Tested:**
  - FluentDockerException (main exception type)
  - FluentDockerNotSupportedException (mutually exclusive operations)
  - SystemException (machine deletion)

### Platform-Specific Testing

- **Windows-Specific:** TemplateString path conversion, isolation technology, PowerShell
- **Unix-Specific:** Script execution paths
- **Conditional Execution:** Many tests skip on certain platforms (IsWindows, IsNative, IsEmulatedNative checks)

### Known Test Limitations

1. **Docker Machine Tests:** All marked [Ignore] as Docker Machine is deprecated
2. **Remote Daemon Tests:** Marked [Ignore], require specific SSH keys and network setup
3. **Multi-Container Definition:** One test marked [Ignore], would require extensive setup
4. **Windows Container Tests:** Some tests marked [Ignore] for Windows-only scenarios
5. **External Service Dependencies:** Tests require PostgreSQL, MySQL, RabbitMQ containers to be functional

---

## Test Execution Requirements

### System Requirements
- Docker daemon running (native, via Machine, or SSH)
- For native tests: Native Docker installation (Windows 10+, Mac, or Linux)
- For remote tests: SSH key configured and reachable host
- ~50GB disk space for image downloads
- Network access for image pulling from Docker Hub

### Docker Hub Images Used
- postgres:9.6-alpine, 10-alpine, 14.2-alpine, latest
- nginx:1.13.6-alpine, latest
- ubuntu:14.04
- alpine
- crccheck/hello-world
- mailhog/mailhog:latest
- mariotoffia/* (custom test images)
- mcr.microsoft.com/* (Windows images)
- rabbitmq, redis, mysql (in compose files)

### Test Execution Time
- **Unit Tests (76):** < 1 minute
- **Integration Tests (87):** 10-30 minutes (depending on image cache and network)
- **Total Suite:** 15-40 minutes

---

## Key Testing Insights

### Strengths
1. **Comprehensive Coverage:** Tests cover CLI, service layer, and Fluent API
2. **Real Integration Testing:** Uses actual Docker daemon, not mocks
3. **Error Message Validation:** Tests ensure error messages are diagnostic
4. **Platform Support:** Tests handle Windows, Mac, and Linux
5. **Edge Cases:** Extensive edge case coverage (timezones, URLs, quotes, special chars)

### Gaps for v3 Implementation
1. **Async/Await:** Current tests don't use async builders extensively
2. **Dependency Injection:** No DI testing patterns observed
3. **Provider Abstraction:** No tests for swappable providers (e.g., Podman)
4. **Performance Testing:** No load/stress tests
5. **Health Checks:** Limited health check testing
6. **Logging:** No structured logging validation

### Migration Considerations for v3
1. Preserve integration test infrastructure (ClassInitialize patterns)
2. Maintain fluent builder API compatibility
3. Add async method variants (don't remove sync versions)
4. Consider provider abstraction testing
5. Expand health check and readiness testing

---

## Conclusion

The v2.x.x test suite contains **163 test methods** across **33 test files**, organized into 7 logical categories. The tests emphasize:

- **Integration-focused:** 108 integration tests require actual Docker execution
- **API validation:** Tests ensure fluent API design is correct and intuitive
- **Error handling:** Comprehensive error scenario coverage
- **Cross-platform:** Support for Windows, Mac, and Linux environments
- **Real services:** Tests use actual Docker, not mocks

This comprehensive test suite provides a solid foundation for v3.x.x development while identifying areas for enhancement such as async patterns, dependency injection, and provider abstraction.
