# TenantOps — Setup

Two paths: **local** (docker-compose, no Azure needed beyond AI) and **Azure
deploy** (Terraform + GitHub Actions). The local path exercises every layer
except Front Door + APIM — the web app calls the services directly and a
subset of the APIM behaviour is replicated by `TenantResolutionMiddleware`.

---

## Prerequisites

| Tool            | Version           | Reason                             |
|-----------------|-------------------|------------------------------------|
| Docker Desktop  | ≥ 4.30            | compose v2, buildx, SQL Edge       |
| .NET SDK        | 8.0.x             | service builds and tests           |
| Node.js         | 20.x              | Next.js build                      |
| Terraform       | ≥ 1.6             | `infra/terraform`                  |
| Azure CLI       | ≥ 2.60            | `az login`, ACR push               |
| `jq`, `curl`    | any recent        | `scripts/seed-documents.sh`        |

You will also need an **Azure OpenAI** resource (or `OpenAI.com` account with
light orchestrator modification) and an **Azure AI Search** service. Both are
required for the chat feature to work — the rest of the stack (tenants, KB
CRUD, tickets, RLS proof) runs without them.

Docs:
- Create Azure OpenAI: https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource
- Create AI Search: https://learn.microsoft.com/azure/search/search-create-service-portal

---

## Local deployment

### 1. Clone and configure

```bash
git clone <your-fork> tenantops && cd tenantops
cp .env.example .env
```

Edit `.env`. Minimum required changes:

```ini
AZURE_OPENAI_ENDPOINT=https://your-aoai.openai.azure.com
AZURE_OPENAI_API_KEY=<key>
AZURE_OPENAI_CHAT_DEPLOYMENT=gpt-4o-mini
AZURE_OPENAI_EMBEDDING_DEPLOYMENT=text-embedding-3-small

AZURE_SEARCH_ENDPOINT=https://your-search.search.windows.net
AZURE_SEARCH_ADMIN_KEY=<admin-key>
```

The Entra variables can stay at placeholders for local dev — the local JWT
fallback handles authentication.

### 2. Start the stack

```bash
make up
```

This runs `docker compose up --build`, waits for SQL to become healthy, then
applies migrations (`001_schema.sql`, `002_rls.sql`) and the seed
(`tenants.sql`). You should see:

```
TenantOps is up:
  Web:        http://localhost:3000
  Contoso:    http://localhost:3000/t/contoso
  Fabrikam:   http://localhost:3000/t/fabrikam
  Tenant API: http://localhost:5001/health
  Core  API:  http://localhost:5002/health
  AI  API:    http://localhost:5003/health
  Identity:   http://localhost:5004/health
```

### 3. Index the seed KB documents

```bash
make seed-docs
```

This calls `scripts/seed-documents.sh`, which registers a demo user under
each tenant, obtains a local JWT, and POSTs to
`ai-orchestrator:/documents/index`. After it completes, the seeded KB
articles for Contoso and Fabrikam are embedded and indexed.

### 4. (Optional) Host-based routing locally

`*.localtest.me` always resolves to `127.0.0.1` globally, so no hosts-file
edit is needed for a basic demo. To use real host headers:

```bash
# /etc/hosts
127.0.0.1 contoso.localtest.me
127.0.0.1 fabrikam.localtest.me
```

Then open `http://contoso.localtest.me:3000` — the middleware will pull the
slug from the subdomain.

### 5. Verify isolation

```bash
dotnet test services/TenantOps.sln --filter RlsIsolationTests
```

Six tests must pass. They prove:
- Each tenant sees only its own documents.
- A cross-tenant SELECT returns zero rows (FILTER predicate).
- A cross-tenant INSERT raises `SqlException` (BLOCK predicate).
- `SESSION_CONTEXT` cannot be overwritten mid-connection (`read_only=1`).
- An unscoped connection returns zero rows.

### Common local issues

- **SQL takes > 30s to start on first boot.** Normal for Azure SQL Edge on
  ARM Macs. `make up` waits 15s; retry `make migrate` if timing out.
- **Port 1433 already in use.** Another SQL instance is running. Stop it or
  change the mapping in `docker-compose.yml`.
- **AI Search returns 403 on index create.** The admin key is wrong or the
  service is still provisioning. Check the portal.

---

## Azure deployment

### 1. Create the Terraform state backend

Terraform stores its state in an Azure Storage account. Create it once per
subscription:

```bash
RG=tenantops-tfstate-rg
SA=tenantopstfstate$(openssl rand -hex 3)   # must be globally unique
LOC=southeastasia

az group create -n $RG -l $LOC
az storage account create -n $SA -g $RG -l $LOC --sku Standard_LRS
az storage container create -n tfstate --account-name $SA --auth-mode login

# Update backend config to match:
sed -i "s/tenantopstfstate/$SA/" infra/terraform/backends/dev.tfvars
```

Ref: https://learn.microsoft.com/azure/developer/terraform/store-state-in-azure-storage

### 2. Create the Entra app registration

Manual step (or use `azuread_application` resources — out of scope for the
demo). You need:

- App registration with audience `api://tenantops`
- A custom optional claim named `tid_app` mapped from the user's tenant id
  (directory extension or app-role claim)
- Redirect URI for the Next.js app: `https://<front-door-host>/auth/callback`
- A client secret (store in Key Vault or a GitHub Actions secret)

Ref: https://learn.microsoft.com/entra/identity-platform/quickstart-register-app

### 3. Configure GitHub Actions OIDC federated credential

The three workflows use federated OIDC — no stored service-principal
secrets. Create a federated credential on your Entra app pointing at your
GitHub org/repo:

```bash
az ad app federated-credential create --id <appObjectId> --parameters '{
  "name": "github-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<org>/<repo>:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Ref: https://learn.microsoft.com/entra/workload-id/workload-identity-federation-create-trust

Add these GitHub secrets (Settings → Secrets and variables → Actions):

| Secret                  | Value                                   |
|-------------------------|-----------------------------------------|
| `AZURE_CLIENT_ID`       | app registration client id              |
| `AZURE_TENANT_ID`       | Entra tenant id                         |
| `AZURE_SUBSCRIPTION_ID` | target subscription                     |
| `SQL_ADMIN_PASSWORD`    | strong random password                  |
| `ENTRA_TENANT_ID`       | Entra tenant id (same as above, for tf) |

Add this variable:

| Variable    | Value                             |
|-------------|-----------------------------------|
| `ACR_NAME`  | will be filled after first apply  |

### 4. First deploy

Local or CI — both work.

**Local:**

```bash
cd infra/terraform
export TF_VAR_sql_admin_password='<strong-password>'
terraform init -backend-config=backends/dev.tfvars
terraform apply -var-file=environments/dev.tfvars
```

**CI:** push to `main`, then trigger the `infra` workflow with
`apply=true`. The `apply` job is gated by a GitHub Environment which you
should configure with required reviewers.

Outputs include:

- `front_door_hostname` — the public entrypoint
- `apim_gateway` — APIM hostname (internal only, via Front Door)
- `acr_login_server` — set this as the `ACR_NAME` variable in GitHub

### 5. Build and push images

Trigger the `images` workflow (push to `main` with changes under
`services/` or `apps/web/`, or `workflow_dispatch`). It builds all five
images in parallel and pushes `:<sha>` and `:latest` tags to ACR.

After the first push, re-run the `infra` workflow so the Container Apps
resources pull the `:latest` tags.

### 6. Run SQL migrations

The migrations aren't in Terraform (intentional — schema changes need
ownership discipline). Run once post-deploy:

```bash
SERVER=$(terraform output -raw sql_server_fqdn)
az sql db show-connection-string --server $SERVER --name tenantops --client sqlcmd

# Use sqlcmd or Azure Data Studio connected with Entra admin credentials
sqlcmd -S $SERVER -d tenantops -G -i infra/sql/migrations/001_schema.sql
sqlcmd -S $SERVER -d tenantops -G -i infra/sql/migrations/002_rls.sql
sqlcmd -S $SERVER -d tenantops -G -i infra/sql/seed/tenants.sql
```

### 7. Attach the managed identity as a SQL user

The contained-user-for-MI pattern requires one post-deploy T-SQL step:

```sql
CREATE USER [<managed-identity-name>] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [<managed-identity-name>];
ALTER ROLE db_datawriter ADD MEMBER [<managed-identity-name>];
GRANT EXECUTE ON SCHEMA::sys TO [<managed-identity-name>];
-- NOTE: do NOT grant db_owner — RLS would be bypassed.
```

Ref: https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-service-principal

### 8. DNS

Point your production apex/subdomain at the Front Door endpoint using a
CNAME. For each customer tenant, register its custom domain on the Front
Door profile and add the domain to `app.Tenants.Domain`.

Ref: https://learn.microsoft.com/azure/frontdoor/front-door-custom-domain

---

## Tearing down

```bash
# Local
make down
make clean    # removes volumes too

# Azure
cd infra/terraform
terraform destroy -var-file=environments/dev.tfvars
```

The `azurerm_cognitive_account` (OpenAI) has a 48-hour soft-delete window;
if you re-apply within that window Terraform will fail on recreate. Either
wait or purge manually via the Azure CLI.
Ref: https://learn.microsoft.com/azure/ai-services/recover-purge-resources
