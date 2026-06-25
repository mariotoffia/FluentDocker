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
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "Category=Unit" --framework net10.0 --configuration Debug --verbosity normal

# Runs the unit suite on net8.0 too (requires the .NET 8 runtime installed). CI runs both.
.PHONY: test-net8
test-net8:
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "Category=Unit" --framework net8.0 --configuration Debug --verbosity normal

.PHONY: test-integration
test-integration:
	@mkdir -p .out/test
	@rm -rf .out/test/integration-test.txt
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "Category=Integration" --configuration Debug --verbosity normal 2>&1 | tee .out/test/integration-test.txt

# Real Docker Model Runner gate. Requires a working `docker model` runtime.
# FLUENTDOCKER_REQUIRE_DMR=1 makes the DMR tests HARD-FAIL instead of self-skipping
# when the runner is missing, so a green run proves real coverage.
.PHONY: test-dmr
test-dmr:
	@mkdir -p .out/test
	@rm -rf .out/test/dmr-test.txt
	FLUENTDOCKER_REQUIRE_DMR=1 dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "Category=Integration&Requires=Dmr" --configuration Debug --verbosity normal 2>&1 | tee .out/test/dmr-test.txt

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
	@mkdir -p .out/test
	@rm -rf .out/test/devlocal-test.txt
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --filter "Category=DevLocal" --configuration Debug --verbosity normal 2>&1 | tee .out/test/devlocal-test.txt

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
	dotnet format whitespace $(SOLUTION) --verify-no-changes
	dotnet format style $(SOLUTION) --verify-no-changes

.PHONY: format
format:
	dotnet format $(SOLUTION)

.PHONY: check
check: lint test

.PHONY: coverage
coverage:
	@mkdir -p .out/coverage
	@rm -rf .out/coverage/*
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj \
		--filter "Category=Unit" \
		--framework net10.0 \
		--configuration Debug \
		--collect:"XPlat Code Coverage" \
		--results-directory .out/coverage \
		--settings coverletArgs.runsettings
	@echo ""
	@echo "Coverage XML written to .out/coverage/"
	@echo "To generate HTML report, install reportgenerator and run:"
	@echo "  dotnet tool install -g dotnet-reportgenerator-globaltool"
	@echo "  reportgenerator -reports:.out/coverage/**/coverage.opencover.xml -targetdir:.out/coverage/html -reporttypes:Html"
	@echo "  open .out/coverage/html/index.html"

# Coverage regression gate (finding M22). Enforces a conservative line/branch FLOOR
# on the report produced by `make coverage`. Override floors via COVERAGE_LINE_MIN /
# COVERAGE_BRANCH_MIN. The XPlat collector cannot fail the build on a threshold itself
# (that is a coverlet.msbuild feature), so the floor is enforced post-collection here.
.PHONY: coverage-check
coverage-check: coverage
	@bash scripts/coverage-threshold

.PHONY: coverage-html
coverage-html: coverage
	reportgenerator \
		-reports:".out/coverage/**/coverage.opencover.xml" \
		-targetdir:.out/coverage/html \
		-reporttypes:Html
	@echo "Coverage report: .out/coverage/html/index.html"

.PHONY: docs
docs:
	cd docs && bundle exec jekyll serve --livereload

.PHONY: docs-install
docs-install:
	cd docs && bundle install

.PHONY: pack
pack: build-release
ifdef VERSION
	dotnet pack FluentDocker/FluentDocker.csproj --configuration Release --no-build /p:Version=$(VERSION)
	dotnet pack FluentDocker.Testing.Xunit/FluentDocker.Testing.Xunit.csproj --configuration Release --no-build /p:Version=$(VERSION)
	dotnet pack FluentDocker.Testing.MsTest/FluentDocker.Testing.MsTest.csproj --configuration Release --no-build /p:Version=$(VERSION)
	dotnet pack FluentDocker.Testing.NUnit/FluentDocker.Testing.NUnit.csproj --configuration Release --no-build /p:Version=$(VERSION)
else
	dotnet pack FluentDocker/FluentDocker.csproj --configuration Release --no-build
	dotnet pack FluentDocker.Testing.Xunit/FluentDocker.Testing.Xunit.csproj --configuration Release --no-build
	dotnet pack FluentDocker.Testing.MsTest/FluentDocker.Testing.MsTest.csproj --configuration Release --no-build
	dotnet pack FluentDocker.Testing.NUnit/FluentDocker.Testing.NUnit.csproj --configuration Release --no-build
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
	@echo "  test-integration - Run integration tests (Category=Integration; requires Docker/Podman)"
	@echo "  test-dmr         - Run real Docker Model Runner tests (requires docker model runtime)"
	@echo "  devlocal-setup   - Start Swarm + Podman machine for DevLocal tests"
	@echo "  devlocal-teardown- Stop Swarm + Podman machine after DevLocal tests"
	@echo "  cleanup-test-resources - Remove stale Docker/Podman test containers"
	@echo "  test-devlocal    - Run DevLocal tests (requires Swarm, local registry, Podman machine)"
	@echo "  benchmark        - Run all benchmarks"
	@echo "  benchmark-stats  - Run container stats benchmarks only"
	@echo "  benchmark-template - Run template string benchmarks only"
	@echo "  lint             - Check code formatting"
	@echo "  format           - Format code"
	@echo "  coverage         - Run unit tests with code coverage (XML output)"
	@echo "  coverage-check   - Run coverage and enforce the line/branch regression floor"
	@echo "  coverage-html    - Generate HTML coverage report (requires reportgenerator)"
	@echo "  docs             - Serve Jekyll docs locally with live reload"
	@echo "  docs-install     - Install Jekyll dependencies for docs"
	@echo "  pack             - Create NuGet packages (use VERSION=x.y.z for versioned packs)"
	@echo "  check            - Run lint + unit tests"
	@echo "  help             - Show this help"
