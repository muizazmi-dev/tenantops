SHELL := /bin/bash
export MSYS_NO_PATHCONV=1

SQL_SA_PASSWORD ?= Your_Strong_Passw0rd!

.PHONY: help up down restart logs build migrate seed seed-docs test clean tf-init tf-plan tf-apply

help:
	@echo "TenantOps — dev tasks"
	@echo "  make up          Start full local stack (web, services, SQL, Azurite)"
	@echo "  make down        Stop and remove containers"
	@echo "  make restart     Rebuild and restart services"
	@echo "  make logs        Tail logs"
	@echo "  make build       Build all docker images"
	@echo "  make migrate     Apply SQL migrations (schema + RLS)"
	@echo "  make seed        Insert seed tenants (Contoso, Fabrikam)"
	@echo "  make seed-docs   Index sample documents via ai-orchestrator"
	@echo "  make test        Run unit + integration tests"
	@echo "  make clean       Remove volumes and containers"
	@echo "  make tf-init     terraform init"
	@echo "  make tf-plan     terraform plan"
	@echo "  make tf-apply    terraform apply"

up:
	docker compose up -d --build
	@echo "Waiting for SQL to be ready..."
	@docker compose exec -T sql bash -c \
	  "until /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'Your_Strong_Passw0rd!' -Q 'SELECT 1' >/dev/null 2>&1; do printf '.'; sleep 2; done && echo ' ready'"
	$(MAKE) migrate
	$(MAKE) seed
	@echo ""
	@echo "TenantOps is up:"
	@echo "  Web:        http://localhost:3000"
	@echo "  Contoso:    http://localhost:3000/t/contoso"
	@echo "  Fabrikam:   http://localhost:3000/t/fabrikam"
	@echo "  Tenant API: http://localhost:5001/health"
	@echo "  Core  API:  http://localhost:5002/health"
	@echo "  AI  API:    http://localhost:5003/health"
	@echo "  Identity:   http://localhost:5004/health"

down:
	docker compose down

restart:
	docker compose down
	docker compose up -d --build

logs:
	docker compose logs -f --tail=100

build:
	docker compose build

migrate:
	@echo "Applying SQL migrations..."
	docker compose exec -T sql /opt/mssql-tools/bin/sqlcmd \
	  -S localhost -U sa -P "$(SQL_SA_PASSWORD)" \
	  -i /migrations/001_schema.sql
	docker compose exec -T sql /opt/mssql-tools/bin/sqlcmd \
	  -S localhost -U sa -P "$(SQL_SA_PASSWORD)" \
	  -d tenantops -i /migrations/002_rls.sql

seed:
	@echo "Seeding tenants..."
	docker compose exec -T sql /opt/mssql-tools/bin/sqlcmd \
	  -S localhost -U sa -P "$(SQL_SA_PASSWORD)" \
	  -d tenantops -i /seed/tenants.sql

seed-docs:
	@echo "Seeding knowledge-base documents..."
	bash ./scripts/seed-documents.sh

test:
	dotnet test services/TenantOps.sln
	cd apps/web && npm test

clean:
	docker compose down -v
	rm -rf apps/web/.next apps/web/node_modules

tf-init:
	cd infra/terraform && terraform init

tf-plan:
	cd infra/terraform && terraform plan -var-file=environments/dev.tfvars

tf-apply:
	cd infra/terraform && terraform apply -var-file=environments/dev.tfvars
