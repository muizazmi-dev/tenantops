-- =============================================================================
-- TenantOps — seed/tenants.sql
-- Demo tenants (ISV, PTC), hidden platform tenant, and platform admin user.
-- Fully idempotent: safe to run on both fresh and existing databases.
-- =============================================================================
USE tenantops;
GO

EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1;
GO

-- ---------------------------------------------------------------------------
-- Tenants — upsert so slug/name changes apply to existing databases
-- ---------------------------------------------------------------------------
DECLARE @isv      UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @ptc      UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @platform UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

MERGE app.Tenants AS tgt
USING (VALUES
    (@isv,      'isv',      'isv.localtest.me',      'ISV',
     '{"primary":"#0b5cad","accent":"#f08a24","logo":"/logos/default.svg"}',      1),
    (@ptc,      'ptc',      'ptc.localtest.me',      'PTC',
     '{"primary":"#2e7d32","accent":"#ffb300","logo":"/logos/default.svg"}',      1),
    (@platform, 'platform', NULL,                    'TenantOps Platform',
     '{}',                                                                          0)
) AS src (TenantId, Slug, Domain, Name, ThemeJson, IsActive)
ON tgt.TenantId = src.TenantId
WHEN MATCHED THEN
    UPDATE SET Slug     = src.Slug,
               Domain   = src.Domain,
               Name     = src.Name,
               ThemeJson= src.ThemeJson,
               IsActive = src.IsActive
WHEN NOT MATCHED THEN
    INSERT (TenantId, Slug, Domain, Name, ThemeJson, IsActive)
    VALUES (src.TenantId, src.Slug, src.Domain, src.Name, src.ThemeJson, src.IsActive);
GO

-- ---------------------------------------------------------------------------
-- Demo users — one per tenant (local-auth; passwords are demo placeholders)
-- ---------------------------------------------------------------------------
EXEC sp_set_session_context @key=N'TenantId', @value='11111111-1111-1111-1111-111111111111';
MERGE app.Users AS tgt
USING (VALUES ('11111111-1111-1111-1111-000000000001',
               '11111111-1111-1111-1111-111111111111',
               'demo@isv.test', 'ISV Demo')) AS src (UserId, TenantId, Email, DisplayName)
ON tgt.TenantId = src.TenantId AND tgt.IdentityProvider = 'local'
WHEN MATCHED THEN
    UPDATE SET Email = src.Email, DisplayName = src.DisplayName
WHEN NOT MATCHED THEN
    INSERT (UserId, TenantId, Email, DisplayName, IdentityProvider, PasswordHash)
    VALUES (src.UserId, src.TenantId, src.Email, src.DisplayName,
            'local', 'AQAAAAIAAYagAAAAELdemo-placeholder-hash');
GO

EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1;
EXEC sp_set_session_context @key=N'TenantId', @value='22222222-2222-2222-2222-222222222222';
MERGE app.Users AS tgt
USING (VALUES ('22222222-2222-2222-2222-000000000001',
               '22222222-2222-2222-2222-222222222222',
               'demo@ptc.test', 'PTC Demo')) AS src (UserId, TenantId, Email, DisplayName)
ON tgt.TenantId = src.TenantId AND tgt.IdentityProvider = 'local'
WHEN MATCHED THEN
    UPDATE SET Email = src.Email, DisplayName = src.DisplayName
WHEN NOT MATCHED THEN
    INSERT (UserId, TenantId, Email, DisplayName, IdentityProvider, PasswordHash)
    VALUES (src.UserId, src.TenantId, src.Email, src.DisplayName,
            'local', 'AQAAAAIAAYagAAAAELdemo-placeholder-hash');
GO

-- ---------------------------------------------------------------------------
-- Platform admin — admin@tenantops.dev / 12345
-- Password is checked via hardcoded constant in /auth/admin-login (demo only).
-- The hash value here is a sentinel; the admin-login endpoint bypasses it.
-- ---------------------------------------------------------------------------
EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1;
IF NOT EXISTS (SELECT 1 FROM app.Users WHERE Email = 'admin@tenantops.dev')
    INSERT INTO app.Users (UserId, TenantId, Email, DisplayName, IdentityProvider, PasswordHash)
    VALUES ('00000000-0000-0000-0000-000000000099',
            '00000000-0000-0000-0000-000000000001',
            'admin@tenantops.dev', 'Platform Admin', 'local', 'platform-admin-sentinel');
GO

-- ---------------------------------------------------------------------------
-- Sample knowledge-base docs
-- ---------------------------------------------------------------------------
EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1;
EXEC sp_set_session_context @key=N'TenantId', @value='11111111-1111-1111-1111-111111111111';
IF NOT EXISTS (SELECT 1 FROM app.Documents WHERE TenantId='11111111-1111-1111-1111-111111111111' AND Title='Refund policy')
    INSERT INTO app.Documents (TenantId, Title, Content)
    VALUES ('11111111-1111-1111-1111-111111111111', 'Refund policy',
            'ISV offers full refunds within 14 days for unused licences. Contact billing@isv.test for exceptions.');
IF NOT EXISTS (SELECT 1 FROM app.Documents WHERE TenantId='11111111-1111-1111-1111-111111111111' AND Title='Service hours')
    INSERT INTO app.Documents (TenantId, Title, Content)
    VALUES ('11111111-1111-1111-1111-111111111111', 'Service hours',
            'ISV support is available Monday to Friday, 9am to 6pm. Emergency support is billed at 1.5x rate.');
GO

EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1;
EXEC sp_set_session_context @key=N'TenantId', @value='22222222-2222-2222-2222-222222222222';
IF NOT EXISTS (SELECT 1 FROM app.Documents WHERE TenantId='22222222-2222-2222-2222-222222222222' AND Title='Refund policy')
    INSERT INTO app.Documents (TenantId, Title, Content)
    VALUES ('22222222-2222-2222-2222-222222222222', 'Refund policy',
            'PTC refunds are issued within 30 days. No refunds on emergency callouts. Store credit available for cancellations within 24 hours.');
IF NOT EXISTS (SELECT 1 FROM app.Documents WHERE TenantId='22222222-2222-2222-2222-222222222222' AND Title='Service hours')
    INSERT INTO app.Documents (TenantId, Title, Content)
    VALUES ('22222222-2222-2222-2222-222222222222', 'Service hours',
            'PTC operates 24/7 with weekend surcharges. Standard hours are Monday to Friday 9am to 5pm.');
GO

PRINT 'Seed complete: ISV, PTC, Platform tenants + admin user.';
GO
