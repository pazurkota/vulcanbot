SOLUTION := vulcanbot.sln
PROJECT  := vulcanbot
AP       ?= ap.json
NAME     ?=

.DEFAULT_GOAL := help

.PHONY: help build test test-unit run up down logs ps image psql register sync migration clean

help: ## Show available commands
	@grep -E '^[a-zA-Z_-]+:.*## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*## "} {printf "  \033[36m%-12s\033[0m %s\n", $$1, $$2}'

## --- .NET ---

build: ## Build the solution
	dotnet build $(SOLUTION)

test: ## Run all tests (database tests need Docker)
	dotnet test $(SOLUTION)

test-unit: ## Run tests that don't need Docker
	dotnet test $(SOLUTION) --filter "FullyQualifiedName~vulcanbot.Test.Service"

run: ## Run the bot locally (needs Postgres: make up)
	cd $(PROJECT) && dotnet run

migration: ## Add an EF Core migration: make migration NAME=AddSomething
	@test -n "$(NAME)" || (echo "Usage: make migration NAME=<MigrationName>" && exit 1)
	dotnet ef migrations add $(NAME) --project $(PROJECT)

clean: ## Remove build output
	dotnet clean $(SOLUTION)

## --- Docker ---

up: ## Start Postgres and the bot in the background
	docker compose up -d --build

down: ## Stop all containers (data stays in the volume)
	docker compose down

logs: ## Follow bot logs
	docker compose logs -f vulcanbot

ps: ## Show container status
	docker compose ps

image: ## Rebuild the bot image
	docker compose build vulcanbot

psql: ## Open psql in the Postgres container
	docker compose exec postgres sh -c 'psql -U "$$POSTGRES_USER" -d "$$POSTGRES_DB"'

register: ## Register a device from the eduVULCAN #ap JSON: make register [AP=ap.json]
	@test -f "$(AP)" || (echo "$(AP) not found — save the #ap value from https://eduvulcan.pl/api/ap there" && exit 1)
	docker compose run --rm --build -T vulcanbot register < $(AP)

sync: ## Fetch homework, exams and the timetable for all registered accounts into the database
	docker compose run --rm --build -T vulcanbot sync
