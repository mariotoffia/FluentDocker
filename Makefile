UNAME_S := $(shell uname -s)
SOLUTION := FluentDocker.sln

.PHONY: all
all: build test

.PHONY: build
build:
	dotnet build $(SOLUTION) --configuration Debug

.PHONY: build-release
build-release:
	dotnet build $(SOLUTION) --configuration Release

.PHONY: act-build
act-build:
	act -j build --env-file .env

.PHONY: dep
dep:
ifeq ($(UNAME_S),Darwin)
	@echo "▶️  Ensuring required .NET SDKs are present (macOS)…"
	@bash scripts/ensure-dotnet-sdks
else
	@echo "ℹ️  Skipping .NET SDK check (host OS: $(UNAME_S))"
endif
	dotnet restore $(SOLUTION)

.PHONY: clean
clean:
	dotnet clean $(SOLUTION)
	rm -rf **/bin **/obj

.PHONY: test
test:
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "Category=Unit" --configuration Debug --verbosity normal

.PHONY: test-integration
test-integration:
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --configuration Debug --verbosity normal

.PHONY: devlocal-setup
devlocal-setup:
	@bash scripts/devlocal-setup

.PHONY: devlocal-teardown
devlocal-teardown:
	@bash scripts/devlocal-teardown

.PHONY: cleanup-test-resources
cleanup-test-resources:
	@bash scripts/cleanup-test-resources

.PHONY: test-devlocal
test-devlocal:
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "Category=DevLocal" --configuration Debug --verbosity normal

.PHONY: benchmark
benchmark:
	dotnet run --project FluentDocker.Benchmarks/FluentDocker.Benchmarks.csproj --configuration Release -- --filter "*"

.PHONY: benchmark-stats
benchmark-stats:
	dotnet run --project FluentDocker.Benchmarks/FluentDocker.Benchmarks.csproj --configuration Release -- --filter "*ContainerStats*"

.PHONY: benchmark-template
benchmark-template:
	dotnet run --project FluentDocker.Benchmarks/FluentDocker.Benchmarks.csproj --configuration Release -- --filter "*TemplateString*"

.PHONY: lint
lint:
	dotnet format $(SOLUTION) --verify-no-changes --verbosity diagnostic

.PHONY: format
format:
	dotnet format $(SOLUTION)

.PHONY: pack
pack: build-release
ifdef VERSION
	dotnet pack FluentDocker/FluentDocker.csproj --configuration Release --no-build /p:Version=$(VERSION)
	dotnet pack FluentDocker.MsTest/FluentDocker.MsTest.csproj --configuration Release --no-build /p:Version=$(VERSION)
	dotnet pack FluentDocker.XUnit/FluentDocker.XUnit.csproj --configuration Release --no-build /p:Version=$(VERSION)
else
	dotnet pack FluentDocker/FluentDocker.csproj --configuration Release --no-build
	dotnet pack FluentDocker.MsTest/FluentDocker.MsTest.csproj --configuration Release --no-build
	dotnet pack FluentDocker.XUnit/FluentDocker.XUnit.csproj --configuration Release --no-build
endif

.PHONY: help
help:
	@echo "Available targets:"
	@echo "  all              - Build and test (default)"
	@echo "  build            - Build solution in Debug mode"
	@echo "  build-release    - Build solution in Release mode"
	@echo "  act-build        - Run build via act (GitHub Actions)"
	@echo "  dep              - Install dependencies and restore packages"
	@echo "  clean            - Clean build artifacts"
	@echo "  test             - Run unit tests only (safe for CI)"
	@echo "  test-integration - Run all tests including integration (requires Docker/Podman)"
	@echo "  devlocal-setup   - Start Swarm + Podman machine for DevLocal tests"
	@echo "  devlocal-teardown- Stop Swarm + Podman machine after DevLocal tests"
	@echo "  cleanup-test-resources - Remove stale Docker/Podman test containers"
	@echo "  test-devlocal    - Run DevLocal tests (requires Swarm, local registry, Podman machine)"
	@echo "  benchmark        - Run all benchmarks"
	@echo "  benchmark-stats  - Run container stats benchmarks only"
	@echo "  benchmark-template - Run template string benchmarks only"
	@echo "  lint             - Check code formatting"
	@echo "  format           - Format code"
	@echo "  pack             - Create NuGet packages (use VERSION=x.y.z for versioned packs)"
	@echo "  help             - Show this help"
