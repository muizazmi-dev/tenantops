# TenantOps — Assumptions & Defaults

Where the original spec left room for interpretation, these are the
defaults we took. Each is tagged with its confirming turn (C1..C5) from
the clarifying questions at kickoff, or marked "inferred" otherwise.

---

## Clarifications confirmed at kickoff

| # | Topic | Choice | Rationale |
|---|-------|--------|-----------|
| C1 | Auth strategy | Entra ID (OIDC) primary, local ASP.NET Identity fallback | Per spec §4.1. The dual JWT scheme in `AuthSetup.cs` makes both paths first-class. |
| C2 | AI Search isolation | Single index + mandatory tenant filter | Cheaper, matches spec default. Invariant checks in `SearchRetriever` make the trade-off acceptable. |
| C3 | Scope of generated code | Full monorepo, all files | Delivered across multiple turns. |
| C4 | Dapr usage depth | Service invocation only | Simplest path that still demonstrates the capability. |
| C5 | Target environment emphasis | Balanced — local and Azure both working end-to-end | Compose stack is fully working; Terraform deploys all layers; neither is exhaustively hardened. |

---

## Concrete defaults

### Tenant catalogue

- **Two seeded tenants:** Contoso HVAC (slug `contoso`, blue theme) and
  Fabrikam Services (slug `fabrikam`, green theme). GUIDs are deterministic
  (`11111111-...` and `22222222-...`) for ease of demo scripting.
- **Domain routing at local dev:** `<slug>.localtest.me` used in preference
  to hosts-file edits. Ref: https://readme.localtest.me/.
- **Branding:** minimal — primary and accent colours plus logo path. In
  production this belongs in a Branding table with uploaded assets.

### Identity

- **Local JWT key rotation:** not implemented. The dev secret in `.env` is
  static. Production deployments must fetch from Key Vault and rotate.
- **Password hashing:** PBKDF2-SHA256, 100 000 iterations, 16-byte salt,
  32-byte derived key. Matches current OWASP guidance. Argon2id would be
  stronger but adds a native dependency.
- **`tid_app` claim:** minted by `identity-api` in the local path. In the
  Entra path the caller must configure a directory extension or app-role
  claim. The demo does not provision Entra objects.

### Database

- **Single Azure SQL database** at SKU S0 (10 DTU). Enough for several
  thousand tenants at low QPS; needs `S1`+ or Hyperscale for real load.
- **RLS predicate function:** inline TVF with `SCHEMABINDING` so the query
  optimiser can push it into plans. Documented recommendation from
  Microsoft Learn.
- **Platform-admin escape hatch:** a second `SESSION_CONTEXT` key
  (`IsPlatformAdmin`) short-circuits the predicate. Used only by
  tenant-api's catalogue endpoints. Audit recommended in production.
- **Connection pool safety:** we rely on SqlClient's connection reset
  clearing `SESSION_CONTEXT` on close. Verified behaviour per MS Learn.

### AI Search

- **Vector dimensions:** 1536 (matches `text-embedding-3-small`). Configurable
  via `AzureSearch:EmbeddingDimensions`.
- **Vector algorithm:** HNSW with default parameters. Good enough for
  demo-scale corpora; production should consider tuning `m`, `efConstruction`.
- **API version:** `2025-09-01` (confirmed stable).
- **Chunking:** 1000-char chunks with 150-char overlap. Simple, language-
  agnostic, but tokenizer-aware chunking is better for production.
- **Authentication:** admin key for the demo. The Terraform module grants
  the app MI `Search Index Data Contributor`; switching to RBAC-only is a
  single config flip (`local_authentication_enabled = false`).

### Azure OpenAI

- **Models:** `gpt-4o-mini` for chat (cost-optimised), `text-embedding-3-small`
  for embeddings. Configurable via `AZURE_OPENAI_*_DEPLOYMENT` and
  `aoai_chat_model` / `aoai_embedding_model` Terraform variables.
- **API version:** `2024-10-21`.
- **Deployed capacity:** 20 TPM chat, 60 TPM embeddings. Adjust in the `ai`
  Terraform module's `sku.capacity`.
- **Region:** `eastus` by default (broad model availability). App resources
  live in `southeastasia` (matches the user's location preference) with
  cross-region calls to AOAI.
- **Authentication:** API key for the demo; MI role assignment already
  present in the Terraform module (`Cognitive Services OpenAI User`) so
  switching to keyless is a code-level change to `DefaultAzureCredential`
  in `ai-orchestrator`'s Program.cs.

### Container Apps

- **Single environment**, all five apps share it. Production may want
  separate envs per tier or per tenant-isolation-level.
- **Dapr app IDs** match service names (`identity-api`, `tenant-api`, etc.).
- **Ingress:** web is external, all APIs are internal. The Next.js calls
  them by internal ACA FQDN.
- **Resource limits:** small — 0.25–0.5 CPU, 0.5–1 GiB RAM per container.
- **Replicas:** 1–5 with KEDA HTTP scaler defaults.

### APIM

- **SKU:** Developer_1. Single instance, no SLA. Production: Standard v2 or
  Premium with zone redundancy.
- **Backend pool:** direct-hostname backends per service. Not load-balanced
  because ACA handles that internally.
- **Subscription required:** true on all APIs. The demo uses the
  auto-generated starter product; production should define per-tier products.
- **Policy templating:** simple `templatefile()` substitution at apply time.
  For richer templating use the `azurerm_api_management_custom_domain` +
  `named_value` pattern.

### Front Door

- **SKU:** Premium (required for managed WAF rules + Private Link origins).
  If cost is a concern, the profile can be downgraded to Standard and the
  managed ruleset replaced with custom rules.
- **WAF mode:** Prevention (block on violation). Flip to Detection for
  initial tuning to avoid false positives.
- **Custom domains:** `var.custom_domains` is empty by default.

### Observability

- **Log Analytics retention:** 30 days. Cost-optimised for demo.
- **Sampling:** none configured — every request is sampled. Production
  may want `AppInsightsSampling` at 100% for errors and 10% for success.
- **Alerts:** not configured. Production should wire at minimum: 5xx rate,
  APIM rate-limit denials, RLS block-predicate errors (as SQL events).

### CI/CD

- **OIDC federated credentials** instead of service-principal secrets in
  GitHub. The workflows assume the credential is created at repo scope;
  for PR-from-fork support the subject template needs broadening.
- **Terraform apply is manual** via `workflow_dispatch` with an `apply`
  boolean, gated on a GitHub Environment with required reviewers. No
  auto-apply on push, by design.
- **Image tags:** both `:<sha>` and `:latest` are pushed. Container Apps
  pick up `:latest` on revision restart. Production should pin SHAs in
  Terraform and make the image update the real deployment trigger.

---

## Known gaps (deliberate; noted for production hardening)

1. **Private networking.** No VNet, no Private Endpoints. `infra/terraform/modules/network/main.tf`
   is a stub with references to the right docs. Adding a VNet with private
   endpoints on SQL, AI Search, AOAI, and an internal ACA environment is
   the single largest production hardening step.
2. **Key Vault integration.** Secrets are env vars today. Container Apps
   secret bindings from Key Vault are a one-liner per service.
3. **Per-tenant custom domains.** Wired for support but not automated. Each
   new tenant currently requires a manual Front Door custom-domain add +
   DNS record + an insert into `app.Tenants`.
4. **Prompt injection defence.** No Azure AI Content Safety, no structured
   output constraint, no input sanitisation on documents. Recommended for
   any real deployment.
5. **Tenant lifecycle.** No tenant creation, deletion, or suspension UI.
   Platform admin can read; writes are direct to SQL.
6. **Backup and DR.** Default Azure SQL PITR (7 days). No cross-region
   failover configured. AI Search has no built-in backup — a re-index is
   needed on recovery.
