SHELL := /bin/sh

DOTNET ?= dotnet
SOLUTION ?= RasHub.sln
CONFIGURATION ?= Release
RID ?= linux-x64
PUBLISH_PROJECT ?= src/RasHub.Web/RasHub.Web.csproj
PUBLISH_DIR ?= artifacts/publish/$(RID)

.DEFAULT_GOAL := release

.PHONY: help submodules submodules-update restore build debug release publish clean dev-up dev-stack-up dev-down

help:
	@echo "Targets:"
	@echo "  make submodules         Initialize missing git submodules"
	@echo "  make submodules-update  Update all git submodules"
	@echo "  make build              Build the solution"
	@echo "  make debug              Build in Debug mode"
	@echo "  make release            Build in Release mode"
	@echo "  make publish            Publish RasHub.Web for RID=$(RID)"
	@echo "  make clean              Clean build outputs"
	@echo "  make dev-up             Start PostgreSQL for IDE development"
	@echo "  make dev-stack-up       Start PostgreSQL and RasHub in containers"
	@echo "  make dev-down           Stop the development stack"
	@echo "  make -C deploy help     Show database and deployment commands"

submodules:
	git submodule update --init --recursive

submodules-update:
	git submodule update --init --remote --recursive

restore: submodules
	$(DOTNET) restore "$(SOLUTION)"

build: restore
	$(DOTNET) build "$(SOLUTION)" \
		--configuration "$(CONFIGURATION)" \
		--no-restore

debug: CONFIGURATION := Debug
debug: build

release: CONFIGURATION := Release
release: build

publish: submodules
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

dev-up dev-stack-up dev-down:
	$(MAKE) -C deploy $@
