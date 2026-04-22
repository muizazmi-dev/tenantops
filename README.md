# TenantOps — Multi-Tenant SaaS Demo on Azure

A reference implementation that demonstrates **every layer** of a modern multi-tenant
SaaS stack on Azure: Client → Edge (Front Door + WAF) → API Gateway (APIM) →
Application (Container Apps + Dapr) → Services → Data (Azure SQL + RLS) → AI (Azure
OpenAI + AI Search RAG) — wired together so you can **prove** tenant isolation end-to-end.

## Repo layout

```
apps/web                   Next.js 14 App Router, tenant-aware SSR
services/identity-api      .NET 8 minimal API (local ASP.NET Identity fallback)
services/tenant-api        .NET 8 minimal API (tenant metadata + branding)
services/core-api          .NET 8 minimal API (tickets, documents CRUD)
services/ai-orchestrator   .NET 8 + Semantic Kernel (chat + RAG)
services/_shared           Cross-service library: tenant middleware, SQL session ctx
infra/terraform            Azure infra as code (AFD, APIM, ACA, SQL, AI Search, AOAI)
infra/apim/policies        APIM policy XML (JWT, tenant header, rate limits)
infra/sql                  Schema + RLS + seed
.github/workflows          CI/CD (build, test, image push, terraform plan/apply)
docs                       Architecture, demo script, assumptions, threat model
tests/integration          RLS isolation and AI Search tenant-filter tests
```

## Quick links

- **[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)** — layer-by-layer breakdown with mermaid
- **[docs/SETUP.md](docs/SETUP.md)** — local and Azure deploy instructions
- **[docs/DEMO.md](docs/DEMO.md)** — step-by-step demo script
- **[docs/THREAT_MODEL.md](docs/THREAT_MODEL.md)** — isolation proof points
- **[docs/ASSUMPTIONS.md](docs/ASSUMPTIONS.md)** — defaults we took

## Run locally in 60 seconds

```bash
cp .env.example .env
make up          # docker-compose up -d + migrations + seed
make seed-docs   # index sample docs for Contoso + Fabrikam
open http://localhost:3000/t/contoso
open http://localhost:3000/t/fabrikam
```

## Deploy to Azure

```bash
cd infra/terraform
terraform init -backend-config=backends/dev.tfvars
terraform apply -var-file=environments/dev.tfvars
```

## Reference docs (validation links)

- Azure Front Door + WAF: https://learn.microsoft.com/azure/frontdoor/
- Azure API Management policies: https://learn.microsoft.com/azure/api-management/api-management-policies
- Azure Container Apps + Dapr: https://learn.microsoft.com/azure/container-apps/dapr-overview
- Azure SQL Row-Level Security: https://learn.microsoft.com/sql/relational-databases/security/row-level-security
- Azure AI Search vector index: https://learn.microsoft.com/azure/search/vector-search-how-to-create-index
- Semantic Kernel (.NET): https://learn.microsoft.com/semantic-kernel/
- Microsoft Entra ID OIDC: https://learn.microsoft.com/entra/identity-platform/v2-protocols-oidc
