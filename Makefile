UNAME_S := $(shell uname -s)

.PHONY: build
build:
	act -j build --env-file .env


.PHONY: dep
dep:
ifeq ($(UNAME_S),Darwin)
	@echo "▶️  Ensuring required .NET SDKs are present (macOS)…"
	@bash scripts/ensure-dotnet-sdks
else
	@echo "ℹ️  Skipping .NET SDK check (host OS: $(UNAME_S))"
endif