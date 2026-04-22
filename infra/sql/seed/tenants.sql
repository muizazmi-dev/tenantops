-- =============================================================================
-- TenantOps — seed/tenants.sql
-- Inserts two demo tenants with distinct branding.
-- Uses a platform-admin session context so the catalogue insert isn't blocked.
-- =============================================================================
USE tenantops;
GO

EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1;
GO

DECLARE @contoso  UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @fabrikam UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

MERGE app.Tenants AS tgt
USING (VALUES
    (@contoso,  'contoso',  'contoso.localtest.me',  'Contoso HVAC',
     '{"primary":"#0b5cad","accent":"#f08a24","logo":"/logos/contoso.svg"}'),
    (@fabrikam, 'fabrikam', 'fabrikam.localtest.me', 'Fabrikam Services',
     '{"primary":"#2e7d32","accent":"#ffb300","logo":"/logos/fabrikam.svg"}')
) AS src (TenantId, Slug, Domain, Name, ThemeJson)
ON tgt.TenantId = src.TenantId
WHEN NOT MATCHED THEN
    INSERT (TenantId, Slug, Domain, Name, ThemeJson)
    VALUES (src.TenantId, src.Slug, src.Domain, src.Name, src.ThemeJson);
GO

-- A demo user per tenant (local-auth; password hash is a placeholder).
EXEC sp_set_session_context @key=N'TenantId', @value='11111111-1111-1111-1111-111111111111';
IF NOT EXISTS (SELECT 1 FROM app.Users WHERE Email = 'demo@contoso.test')
    INSERT INTO app.Users (TenantId, Email, DisplayName, IdentityProvider, PasswordHash)
    VALUES ('11111111-1111-1111-1111-111111111111', 'demo@contoso.test', 'Contoso Demo',
            'local', 'AQAAAAIAAYagAAAAELdemo-placeholder-hash');
GO

EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1;
EXEC sp_set_session_context @key=N'TenantId', @value='22222222-2222-2222-2222-222222222222';
IF NOT EXISTS (SELECT 1 FROM app.Users WHERE Email = 'demo@fabrikam.test')
    INSERT INTO app.Users (TenantId, Email, DisplayName, IdentityProvider, PasswordHash)
    VALUES ('22222222-2222-2222-2222-222222222222', 'demo@fabrikam.test', 'Fabrikam Demo',
            'local', 'AQAAAAIAAYagAAAAELdemo-placeholder-hash');
GO

-- Sample knowledge-base docs, one per tenant
EXEC sp_set_session_context @key=N'TenantId', @value='11111111-1111-1111-1111-111111111111';
IF NOT EXISTS (SELECT 1 FROM app.Documents WHERE TenantId='11111111-1111-1111-1111-111111111111' AND Title='Refund policy')
    INSERT INTO app.Documents (TenantId, Title, Content)
    VALUES ('11111111-1111-1111-1111-111111111111',
            'Refund policy',
            'Contoso HVAC offers full refunds within 14 days for unopened parts. Labour is non-refundable once work has started. Contact billing@contoso.test for exceptions.');

IF NOT EXISTS (SELECT 1 FROM app.Documents WHERE TenantId='11111111-1111-1111-1111-111111111111' AND Title='Service hours')
    INSERT INTO app.Documents (TenantId, Title, Content)
    VALUES ('11111111-1111-1111-1111-111111111111',
            'Service hours',
            'Contoso HVAC technicians are available Monday to Saturday, 8am to 6pm. Emergency after-hours service is available at 1.5x rate.');
GO

EXEC sp_set_session_context @key=N'IsPlatformAdmin', @value=1;
EXEC sp_set_session_context @key=N'TenantId', @value='22222222-2222-2222-2222-222222222222';
IF NOT EXISTS (SELECT 1 FROM app.Documents WHERE TenantId='22222222-2222-2222-2222-222222222222' AND Title='Refund policy')
    INSERT INTO app.Documents (TenantId, Title, Content)
    VALUES ('22222222-2222-2222-2222-222222222222',
            'Refund policy',
            'Fabrikam Services refunds are issued within 30 days. No refunds on emergency callouts. Store credit is available for cancellations within 24 hours of appointment.');

IF NOT EXISTS (SELECT 1 FROM app.Documents WHERE TenantId='22222222-2222-2222-2222-222222222222' AND Title='Service hours')
    INSERT INTO app.Documents (TenantId, Title, Content)
    VALUES ('22222222-2222-2222-2222-222222222222',
            'Service hours',
            'Fabrikam Services operates 24/7 with weekend surcharges. Standard hours are Monday to Friday 9am to 5pm.');
GO

PRINT 'Seed complete: 2 tenants, 2 users, 4 documents.';
GO
