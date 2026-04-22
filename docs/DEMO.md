# TenantOps — Demo Script

A 15–20 minute walkthrough that exercises every layer and proves tenant
isolation at three independent checkpoints. Designed for the local docker-
compose stack; the Azure walkthrough maps 1:1 with one addition (WAF
inspection in Front Door).

Pre-demo checklist:

- [ ] `make up` succeeded and all four `/health` endpoints return 200
- [ ] `make seed-docs` completed (Contoso + Fabrikam KB indexed)
- [ ] Browser has DevTools open, Network tab visible
- [ ] A SQL client connected to `localhost:1433` (sa for demonstration only)

---

## Part 1 — Client layer: tenant-aware SSR (2 min)

1. Open `http://localhost:3000`. Show the landing page with two tenant
   cards.
2. Click **Contoso HVAC**. Observe:
   - URL becomes `/t/contoso`
   - Header bar turns **blue** (Contoso's `primary` colour from
     `app.Tenants.ThemeJson`)
   - Tenant name "Contoso HVAC" appears in the header
   - Footer shows `tenant-id: 11111111-...`
3. Click back, then **Fabrikam Services**. Observe:
   - Header bar turns **green** (Fabrikam's theme)
   - Tenant-id in footer is now `22222222-...`

**Talk track.** The Next.js middleware parses the route, sets an
`x-resolved-tenant-slug` header, the root layout calls `tenant-api:/resolve`
via a React-cached fetch, and the resolved theme is injected as CSS
custom properties on `<body>`. One render, one API call, full branding
swap. Production uses the `Host` header; `/t/{slug}` is the local-dev
fallback.

## Part 2 — Edge layer: WAF + security headers (2 min)

*Azure-only; skip on local demo.*

1. In the browser Network tab, show response headers on any page:
   - `Strict-Transport-Security: max-age=63072000; includeSubDomains; preload`
   - `X-Frame-Options: DENY`
   - `X-Content-Type-Options: nosniff`
2. In Azure Portal, open the Front Door profile → Security policies.
   Show the DRS 2.1 managed ruleset and the Bot Manager 1.1 attachment.
3. Run `curl -I https://<front-door>.azurefd.net/?id=1%27+OR+1%3D1--` and
   show the `403` response produced by the WAF's SQLi rule.

## Part 3 — Gateway layer: APIM tenant injection (3 min)

1. Open a terminal, run:

   ```bash
   # Get a Contoso JWT
   TOKEN=$(curl -s -X POST http://localhost:5004/auth/login \
     -H 'content-type: application/json' \
     -d '{"tenantId":"11111111-1111-1111-1111-111111111111",
          "email":"seed@contoso.test","password":"SeedPass!42"}' \
     | jq -r '.access_token')
   echo $TOKEN | cut -d. -f2 | base64 -d 2>/dev/null | jq
   ```
   Show the decoded claims — highlight `tid_app`.

2. Call `core-api` directly with that token:

   ```bash
   curl -s http://localhost:5002/tickets -H "authorization: Bearer $TOKEN" | jq
   ```

3. **Attempt forgery.** Call with a bogus tenant header:

   ```bash
   curl -s http://localhost:5002/tickets \
     -H "authorization: Bearer $TOKEN" \
     -H "x-tenant-id: 22222222-2222-2222-2222-222222222222"
   ```
   Expected: `403 Forbidden — Tenant context mismatch.` The
   `TenantResolutionMiddleware` rejected the request because the header
   disagreed with the JWT claim.

4. Show `infra/apim/policies/api-authenticated.xml`. Highlight three lines:

   ```xml
   <set-header name="x-tenant-id" exists-action="delete" />
   ...
   <validate-jwt .../>
   ...
   <set-header name="x-tenant-id" exists-action="override">
     <value>@((string)context.Variables["appTenantId"])</value>
   </set-header>
   ```

**Talk track.** APIM strips any caller-supplied `x-tenant-id` before validate-
jwt runs. The `tid_app` claim is extracted from the *signed* token, and only
then is `x-tenant-id` written. The backend then requires the header and the
JWT claim to agree — a two-signal design that closes both the header-only
and the token-substitution attacks.

## Part 4 — Data layer: RLS proof (4 min)

This is the centrepiece. Open a SQL client connected as `tenantops_app`:

```
Server:   localhost,1433
Database: tenantops
User:     tenantops_app
Password: App_Strong_Passw0rd!
```

1. **Nothing is visible by default.**
   ```sql
   SELECT COUNT(*) FROM app.Documents;   -- 0
   ```
   *Talk track.* Without `SESSION_CONTEXT`, the predicate evaluates to
   `NULL = TenantId`, which is never true. Zero rows.

2. **Set Contoso context, see Contoso docs.**
   ```sql
   EXEC sp_set_session_context @key=N'TenantId',
     @value='11111111-1111-1111-1111-111111111111', @read_only=1;
   SELECT TenantId, Title FROM app.Documents;
   ```

3. **Try to read Fabrikam data.** Same connection:
   ```sql
   SELECT TenantId, Title FROM app.Documents
   WHERE TenantId = '22222222-2222-2222-2222-222222222222';
   ```
   Zero rows. *The WHERE clause matched rows that the FILTER predicate
   hid.*

4. **Try to insert a Fabrikam row while connected as Contoso:**
   ```sql
   INSERT INTO app.Documents (DocumentId, TenantId, Title, Content)
   VALUES (NEWID(),
           '22222222-2222-2222-2222-222222222222',
           'evil', 'cross-tenant insert');
   ```
   Expected: `The attempted insert failed because the target object
   '<...>' has a BLOCK AFTER INSERT PREDICATE...`

5. **Try to switch tenants on this connection:**
   ```sql
   EXEC sp_set_session_context @key=N'TenantId',
     @value='22222222-2222-2222-2222-222222222222';
   ```
   Expected: `The key 'TenantId' was set as read_only for this session...`
   The only way to "become" Fabrikam is to open a new connection with a
   Fabrikam-scoped session context — which means the web request itself
   must authenticate as Fabrikam.

Run the automated proof:
```bash
dotnet test services/TenantOps.sln --filter RlsIsolationTests
```
All six tests green.

## Part 5 — Service layer: tickets CRUD, no tenant filter in code (2 min)

1. In the Contoso UI (`/t/contoso/tickets`) sign in (email
   `seed@contoso.test`, password `SeedPass!42`) and create a ticket titled
   "AC unit leaking".
2. Show `services/core-api/Program.cs`. Highlight:

   ```csharp
   app.MapGet("/tickets", async (ITenantDbConnectionFactory db) =>
   {
       await using var conn = await db.OpenAsync();
       var rows = await conn.QueryAsync<Ticket>(@"
           SELECT TicketId, TenantId, Title, Body, Status, CreatedAt, UpdatedAt
           FROM app.Tickets ORDER BY CreatedAt DESC;");
       return Results.Ok(rows);
   });
   ```

3. Point out that this query has **no `WHERE TenantId` clause**. The
   tenant scoping is entirely enforced by RLS. If a junior dev forgot the
   filter in an ad-hoc report, they'd still be safe.

## Part 6 — AI layer: RAG with tenant filter (4 min)

1. Go to `/t/contoso/chat`. Sign in if needed. Ask: **"What's our refund
   policy?"**
   - Answer cites Contoso's "Refund policy" document: *"full refunds within
     14 days for unopened parts. Labour is non-refundable..."*
   - Sources panel shows the document title and a snippet.
2. In another tab go to `/t/fabrikam/chat`. Sign in as Fabrikam demo user
   and ask the same question.
   - Answer cites Fabrikam's (different) refund policy: *"refunds within
     30 days, no refunds on emergency callouts..."*
3. Open `services/ai-orchestrator/Search/SearchRetriever.cs`. Highlight:

   ```csharp
   private static string BuildTenantFilter(Guid tenantId) =>
       $"tenantId eq '{tenantId}'";

   private static void AssertFilterIsTenantScoped(string filter, Guid tenantId)
   {
       var expected = $"tenantId eq '{tenantId}'";
       if (!filter.Contains(expected, StringComparison.Ordinal))
           throw new InvalidOperationException(
               "Tenant filter invariant broken — refusing to query.");
   }
   ```

**Talk track.** Three isolation layers on one query:
- The filter is built *inside* the retriever — callers can't omit it.
- An invariant check fires before the HTTP call.
- The tenantId itself came from `TenantContext`, which was populated by the
  middleware's JWT-claim + header-agreement check.

## Part 7 — Observability: follow a request across layers (2 min)

1. Tail logs:
   ```bash
   docker compose logs -f --tail=50 web core-api ai-orchestrator
   ```
2. Submit a chat question. In the logs, point out the shared
   `CorrelationId` and `TenantId` appearing in every service's log scope —
   web, APIM (in Azure), core, ai.
3. In Azure, show App Insights *Transaction search* with the correlation
   id: a flame graph of the request touching Front Door, APIM, Container
   Apps, AI Search, and Azure OpenAI.

## Part 8 — Wrap-up (1 min)

Summary slide:

- **Three isolation layers.** API gateway (JWT + header agreement), SQL
  (RLS via `SESSION_CONTEXT`), AI Search (invariant-checked filter).
- **RAG grounded per tenant.** Same prompt, different answers, different
  citations — proved live.
- **Everything in code.** Terraform + GitHub Actions means a second
  environment is a 10-minute bootstrap, not a 10-day project.

Take questions.
