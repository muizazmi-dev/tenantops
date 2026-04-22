-- =============================================================================
-- TenantOps — 001_schema.sql
-- Creates database, schema, and core tables. Idempotent where practical.
-- Docs: https://learn.microsoft.com/sql/relational-databases/security/row-level-security
-- =============================================================================

IF DB_ID('tenantops') IS NULL
BEGIN
    CREATE DATABASE tenantops;
END;
GO

USE tenantops;
GO

IF SCHEMA_ID('app') IS NULL EXEC('CREATE SCHEMA app');
GO

-- ---------------------------------------------------------------------------
-- Tenants: one row per customer organisation. Slug + Domain used for routing.
-- ---------------------------------------------------------------------------
IF OBJECT_ID('app.Tenants', 'U') IS NULL
BEGIN
    CREATE TABLE app.Tenants (
        TenantId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY DEFAULT NEWID(),
        Slug        NVARCHAR(64)  NOT NULL,
        Domain      NVARCHAR(253) NULL,
        Name        NVARCHAR(200) NOT NULL,
        ThemeJson   NVARCHAR(MAX) NOT NULL CONSTRAINT DF_Tenants_Theme DEFAULT (N'{}'),
        CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_Tenants_Created DEFAULT SYSUTCDATETIME(),
        IsActive    BIT           NOT NULL CONSTRAINT DF_Tenants_Active DEFAULT 1,
        CONSTRAINT UQ_Tenants_Slug   UNIQUE (Slug),
        CONSTRAINT UQ_Tenants_Domain UNIQUE (Domain)
    );
END;
GO

-- ---------------------------------------------------------------------------
-- Users: local identity fallback. Entra users are also mirrored here so we
-- can attach TenantId regardless of IdP. Email is unique *per tenant*.
-- ---------------------------------------------------------------------------
IF OBJECT_ID('app.Users', 'U') IS NULL
BEGIN
    CREATE TABLE app.Users (
        UserId            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Users PRIMARY KEY DEFAULT NEWID(),
        TenantId          UNIQUEIDENTIFIER NOT NULL,
        Email             NVARCHAR(320)    NOT NULL,
        DisplayName       NVARCHAR(200)    NULL,
        IdentityProvider  NVARCHAR(32)     NOT NULL,  -- 'entra' | 'local'
        ExternalSubject   NVARCHAR(200)    NULL,      -- oid/sub from IdP
        PasswordHash      NVARCHAR(500)    NULL,      -- null for entra users
        CreatedAt         DATETIME2(3)     NOT NULL CONSTRAINT DF_Users_Created DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Users_Tenants FOREIGN KEY (TenantId) REFERENCES app.Tenants(TenantId),
        CONSTRAINT UQ_Users_Tenant_Email UNIQUE (TenantId, Email)
    );
END;
GO

-- ---------------------------------------------------------------------------
-- Tickets: service requests. Always carries TenantId for RLS.
-- ---------------------------------------------------------------------------
IF OBJECT_ID('app.Tickets', 'U') IS NULL
BEGIN
    CREATE TABLE app.Tickets (
        TicketId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Tickets PRIMARY KEY DEFAULT NEWID(),
        TenantId    UNIQUEIDENTIFIER NOT NULL,
        Title       NVARCHAR(300)    NOT NULL,
        Body        NVARCHAR(MAX)    NULL,
        Status      NVARCHAR(32)     NOT NULL CONSTRAINT DF_Tickets_Status DEFAULT (N'open'),
        CreatedBy   UNIQUEIDENTIFIER NULL,
        CreatedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_Tickets_Created DEFAULT SYSUTCDATETIME(),
        UpdatedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_Tickets_Updated DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Tickets_Tenants FOREIGN KEY (TenantId) REFERENCES app.Tenants(TenantId)
    );

    CREATE INDEX IX_Tickets_Tenant_Status ON app.Tickets(TenantId, Status)
        INCLUDE (Title, CreatedAt);
END;
GO

-- ---------------------------------------------------------------------------
-- Documents: knowledge-base entries. Source of truth; indexed into AI Search.
-- ---------------------------------------------------------------------------
IF OBJECT_ID('app.Documents', 'U') IS NULL
BEGIN
    CREATE TABLE app.Documents (
        DocumentId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Documents PRIMARY KEY DEFAULT NEWID(),
        TenantId    UNIQUEIDENTIFIER NOT NULL,
        Title       NVARCHAR(300)    NOT NULL,
        Content     NVARCHAR(MAX)    NOT NULL,
        CreatedBy   UNIQUEIDENTIFIER NULL,
        CreatedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_Documents_Created DEFAULT SYSUTCDATETIME(),
        IndexedAt   DATETIME2(3)     NULL,
        CONSTRAINT FK_Documents_Tenants FOREIGN KEY (TenantId) REFERENCES app.Tenants(TenantId)
    );

    CREATE INDEX IX_Documents_Tenant ON app.Documents(TenantId)
        INCLUDE (Title, CreatedAt, IndexedAt);
END;
GO

-- ---------------------------------------------------------------------------
-- Low-privilege app login. Services connect as this principal so RLS cannot
-- be silently bypassed (sa/db_owner bypass RLS). Password is dev-only.
-- In Azure we switch this to a user-assigned managed identity contained user.
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'tenantops_app')
BEGIN
    CREATE LOGIN tenantops_app WITH PASSWORD = 'App_Strong_Passw0rd!', CHECK_POLICY = OFF;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'tenantops_app')
BEGIN
    CREATE USER tenantops_app FOR LOGIN tenantops_app;
    GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::app TO tenantops_app;
END;
GO
