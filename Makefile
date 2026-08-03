SHELL := /bin/sh

DOTNET ?= dotnet
SOLUTION ?= RasHub.sln
CONFIGURATION ?= Release
RID ?= linux-x64
PUBLISH_PROJECT ?=
PUBLISH_DIR ?= artifacts/publish/$(RID)

.DEFAULT_GOAL := release

.PHONY: all help submodules submodules-update restore build debug release publish clean

all: release

help:
	@echo "Targets:"
	@echo "  make submodules         Initialize missing git submodules"
	@echo "  make submodules-update  Pull the configured remote branch for every submodule"
	@echo "  make build              Build the solution (CONFIGURATION=Release by default)"
	@echo "  make debug              Build the solution in Debug mode"
	@echo "  make release            Build the solution in Release mode"
	@echo "  make publish PUBLISH_PROJECT=src/path/App.csproj [RID=linux-x64]"
	@echo "                           Publish a self-contained single-file executable"
	@echo "  make clean              Clean solution build outputs"

submodules:
	git submodule update --init --recursive

submodules-update:
	git submodule update --init --remote --recursive

restore: submodules
	$(DOTNET) restore "$(SOLUTION)"

build: restore
	$(DOTNET) build "$(SOLUTION)" --configuration "$(CONFIGURATION)" --no-restore

debug: CONFIGURATION := Debug
debug: build

release: CONFIGURATION := Release
release: build

publish: submodules
	@test -n "$(PUBLISH_PROJECT)" || { \
		echo "PUBLISH_PROJECT is required and must point to an executable .NET project." >&2; \
		echo "Example: make publish PUBLISH_PROJECT=src/RasHub.App/RasHub.App.csproj RID=$(RID)" >&2; \
		exit 2; \
	}
	$(DOTNET) publish "$(PUBLISH_PROJECT)" \
		--configuration Release \
		--runtime "$(RID)" \
		--self-contained true \
		--output "$(PUBLISH_DIR)" \
		-p:PublishSingleFile=true \
		-p:IncludeNativeLibrariesForSelfExtract=true \
		-p:IncludeAllContentForSelfExtract=true \
		-p:EnableCompressionInSingleFile=true \
		-p:DebugType=None \
		-p:DebugSymbols=false

clean:
	$(DOTNET) clean "$(SOLUTION)"
