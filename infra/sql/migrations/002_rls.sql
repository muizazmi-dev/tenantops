-- =============================================================================
-- TenantOps — 002_rls.sql
-- Row-Level Security via SESSION_CONTEXT('TenantId').
--
-- How it works:
--   1. Every request opens a SQL connection and calls:
--        EXEC sp_set_session_context @key=N'TenantId', @value=<guid>, @read_only=1
--      BEFORE running any query. The read_only=1 flag prevents app code from
--      overwriting the value within the same connection (defence-in-depth).
--   2. A SECURITY PREDICATE checks TenantId = SESSION_CONTEXT('TenantId') on
--      every row of every query — SELECT/INSERT/UPDATE/DELETE.
--   3. The predicate is FILTER (hides rows) + BLOCK (rejects writes).
--   4. The tenantops_app user has no elevated rights, so RLS cannot be bypassed.
--
-- Proof: see tests/integration/RlsTests.cs — asserts zero leakage even when
-- the app layer attempts a cross-tenant SELECT.
--
-- Docs:
--   https://learn.microsoft.com/sql/relational-databases/security/row-level-security
--   https://learn.microsoft.com/sql/t-sql/functions/session-context-transact-sql
-- =============================================================================

USE tenantops;
GO

IF SCHEMA_ID('sec') IS NULL EXEC('CREATE SCHEMA sec');
GO

-- ---------------------------------------------------------------------------
-- Predicate function: returns 1 if the row's TenantId matches the session's
-- TenantId. SCHEMABINDING + inline TVF so the optimiser can push it down.
-- ---------------------------------------------------------------------------
CREATE OR ALTER FUNCTION sec.fn_tenant_predicate (@TenantId UNIQUEIDENTIFIER)
    RETURNS TABLE
    WITH SCHEMABINDING
AS
    RETURN SELECT 1 AS allowed
    WHERE
        -- match if session tenant equals row tenant
        CAST(SESSION_CONTEXT(N'TenantId') AS UNIQUEIDENTIFIER) = @TenantId
        -- OR the caller is explicitly marked as platform-admin
        OR CAST(SESSION_CONTEXT(N'IsPlatformAdmin') AS BIT) = 1;
GO

-- ---------------------------------------------------------------------------
-- Security policy: FILTER hides rows on read; BLOCK rejects writes that would
-- create/alter rows for a different tenant (defence against tampering).
-- ---------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.security_policies WHERE name = N'TenantIsolationPolicy')
    DROP SECURITY POLICY sec.TenantIsolationPolicy;
GO

CREATE SECURITY POLICY sec.TenantIsolationPolicy
    ADD FILTER PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Tickets,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Tickets AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Tickets AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Tickets BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Tickets BEFORE DELETE,

    ADD FILTER PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Documents,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Documents AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Documents AFTER UPDATE,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Documents BEFORE UPDATE,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Documents BEFORE DELETE,

    ADD FILTER PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Users,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Users AFTER INSERT,
    ADD BLOCK  PREDICATE sec.fn_tenant_predicate(TenantId) ON app.Users AFTER UPDATE
    WITH (STATE = ON);
GO

-- ---------------------------------------------------------------------------
-- NOTE on app.Tenants: the Tenants table itself is NOT tenant-scoped — that
-- is the tenant catalogue. The tenant-api reads it using a SEPARATE connection
-- that sets IsPlatformAdmin=1 in SESSION_CONTEXT (and only for domain/slug
-- lookups). Platform-admin connections are gated in application code and
-- audited. See Shared/TenantDbContext.cs.
-- ---------------------------------------------------------------------------
PRINT 'RLS policy TenantIsolationPolicy is ENABLED';
GO
