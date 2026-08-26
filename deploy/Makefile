SHELL := /bin/sh

DOTNET ?= dotnet
DOCKER ?= docker
CONFIGURATION ?= Release
RASHUB_IMAGE ?= rashub:latest

EF_PROJECT ?= ../src/RasHub.Infrastructure/RasHub.Infrastructure.csproj
EF_STARTUP_PROJECT ?= ../src/RasHub.Web/RasHub.Web.csproj
EF_ENVIRONMENT ?= Development
MIGRATION_NAME ?=

DEV_COMPOSE_FILE ?= compose.development.yaml
DEV_ENV_FILE ?= environments/.env.development.example
PROD_COMPOSE_FILE ?= compose.production.yaml
PROD_BUILD_COMPOSE_FILE ?= compose.production.build.yaml
PROD_ENV_FILE ?= environments/.env.production

.DEFAULT_GOAL := help

.PHONY: help tools ef-build migrations-add database-update docker-build dev-config dev-up dev-stack-up dev-down prod-env prod-config prod-up prod-down

help:
	@echo "Database targets:"
	@echo "  make migrations-add MIGRATION_NAME=Name  Add an EF Core migration"
	@echo "  make database-update                     Apply migrations"
	@echo ""
	@echo "Development targets:"
	@echo "  make dev-up             Start PostgreSQL and Seq for local IDE development"
	@echo "  make dev-stack-up       Build and start PostgreSQL, Seq, and RasHub"
	@echo "  make dev-down           Stop the development stack"
	@echo ""
	@echo "Production targets:"
	@echo "  make docker-build       Build the application image"
	@echo "  make prod-config        Validate the local production configuration"
	@echo "  make prod-up            Build, migrate, and start the stack"
	@echo "  make prod-down          Stop the production stack"

tools:
	$(DOTNET) tool restore

ef-build: tools
	$(DOTNET) build "$(EF_STARTUP_PROJECT)" \
		--configuration "$(CONFIGURATION)" \
		-m:1

migrations-add: ef-build
	@test -n "$(MIGRATION_NAME)" || { \
		echo "MIGRATION_NAME is required. Example: make migrations-add MIGRATION_NAME=InitialCreate" >&2; \
		exit 2; \
	}
	$(DOTNET) ef migrations add "$(MIGRATION_NAME)" \
		--no-build \
		--configuration "$(CONFIGURATION)" \
		--project "$(EF_PROJECT)" \
		--startup-project "$(EF_STARTUP_PROJECT)" \
		--output-dir Database/Migrations \
		-- --environment "$(EF_ENVIRONMENT)"

database-update: ef-build
	$(DOTNET) ef database update \
		--no-build \
		--configuration "$(CONFIGURATION)" \
		--project "$(EF_PROJECT)" \
		--startup-project "$(EF_STARTUP_PROJECT)" \
		-- --environment "$(EF_ENVIRONMENT)"

docker-build:
	$(DOCKER) build \
		--file Dockerfile \
		--tag "$(RASHUB_IMAGE)" \
		..

dev-config:
	$(DOCKER) compose \
		--env-file "$(DEV_ENV_FILE)" \
		--file "$(DEV_COMPOSE_FILE)" \
		config --quiet

dev-up: dev-config
	$(DOCKER) compose \
		--env-file "$(DEV_ENV_FILE)" \
		--file "$(DEV_COMPOSE_FILE)" \
		stop rashub
	$(DOCKER) compose \
		--env-file "$(DEV_ENV_FILE)" \
		--file "$(DEV_COMPOSE_FILE)" \
		up --detach --remove-orphans postgres seq

dev-stack-up: dev-config
	$(DOCKER) compose \
		--env-file "$(DEV_ENV_FILE)" \
		--file "$(DEV_COMPOSE_FILE)" \
		up --detach --build --remove-orphans

dev-down:
	$(DOCKER) compose \
		--env-file "$(DEV_ENV_FILE)" \
		--file "$(DEV_COMPOSE_FILE)" \
		down --remove-orphans

prod-env:
	@test -f "$(PROD_ENV_FILE)" || { \
		echo "$(PROD_ENV_FILE) is required. Copy environments/.env.production.example and replace all secrets." >&2; \
		exit 2; \
	}

prod-config: prod-env
	$(DOCKER) compose \
		--env-file "$(PROD_ENV_FILE)" \
		--file "$(PROD_COMPOSE_FILE)" \
		--file "$(PROD_BUILD_COMPOSE_FILE)" \
		config --quiet

prod-up: prod-config
	$(DOCKER) compose \
		--env-file "$(PROD_ENV_FILE)" \
		--file "$(PROD_COMPOSE_FILE)" \
		--file "$(PROD_BUILD_COMPOSE_FILE)" \
		up --detach --build --remove-orphans

prod-down: prod-env
	$(DOCKER) compose \
		--env-file "$(PROD_ENV_FILE)" \
		--file "$(PROD_COMPOSE_FILE)" \
		down --remove-orphans
