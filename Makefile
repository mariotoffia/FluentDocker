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
	@mkdir -p .out/test
	@rm -rf .out/test/integration-test.txt
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj --configuration Debug --verbosity normal 2>&1 | tee .out/test/integration-test.txt

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
	dotnet test FluentDocker.Tests/FluentDocker.Tests.csproj \
		--filter "Category=Unit" \
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
	@echo "  coverage         - Run unit tests with code coverage (XML output)"
	@echo "  coverage-html    - Generate HTML coverage report (requires reportgenerator)"
	@echo "  docs             - Serve Jekyll docs locally with live reload"
	@echo "  docs-install     - Install Jekyll dependencies for docs"
	@echo "  pack             - Create NuGet packages (use VERSION=x.y.z for versioned packs)"
	@echo "  check            - Run lint + unit tests"
	@echo "  help             - Show this help"
