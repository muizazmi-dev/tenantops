variable "name_prefix" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "admin_login" { type = string }
variable "admin_password" { type = string, sensitive = true }
variable "sku" { type = string }
variable "app_identity_object_id" { type = string }
variable "app_identity_client_id" { type = string }
variable "app_identity_name" { type = string }
variable "log_analytics_id" { type = string }
variable "tags" { type = map(string) }

# ---------------------------------------------------------------------------
# SQL Server with Entra admin wired to the caller (so you can create contained
# users for managed identities post-deploy).
# Docs: https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-overview
# ---------------------------------------------------------------------------
data "azuread_client_config" "current" {}

resource "azurerm_mssql_server" "main" {
  name                          = "${var.name_prefix}-sql"
  location                      = var.location
  resource_group_name           = var.resource_group_name
  version                       = "12.0"
  administrator_login           = var.admin_login
  administrator_login_password  = var.admin_password
  minimum_tls_version           = "1.2"
  public_network_access_enabled = true  # restrict with firewall rules below

  azuread_administrator {
    login_username              = "aad-admin"
    object_id                   = data.azuread_client_config.current.object_id
    azuread_authentication_only = false
  }

  tags = var.tags
}

# Allow Azure services (Container Apps) during initial bring-up. Tighten in prod.
resource "azurerm_mssql_firewall_rule" "azure_services" {
  name             = "AllowAzureServices"
  server_id        = azurerm_mssql_server.main.id
  start_ip_address = "0.0.0.0"
  end_ip_address   = "0.0.0.0"
}

resource "azurerm_mssql_database" "main" {
  name                 = "tenantops"
  server_id            = azurerm_mssql_server.main.id
  sku_name             = var.sku
  zone_redundant       = false
  storage_account_type = "Zone"
  tags                 = var.tags
}

# Diagnostics → Log Analytics
resource "azurerm_monitor_diagnostic_setting" "sql" {
  name                       = "diag-sql"
  target_resource_id         = azurerm_mssql_database.main.id
  log_analytics_workspace_id = var.log_analytics_id

  enabled_log { category = "SQLSecurityAuditEvents" }
  enabled_log { category = "Errors" }
  metric      { category = "AllMetrics" }
}

output "server_fqdn" { value = azurerm_mssql_server.main.fully_qualified_domain_name }
output "database_name" { value = azurerm_mssql_database.main.name }

# Connection string using AAD managed-identity. The .NET services acquire a
# token via DefaultAzureCredential and pass it as AccessToken on SqlConnection.
output "connection_string_for_managed_identity" {
  value = "Server=tcp:${azurerm_mssql_server.main.fully_qualified_domain_name},1433;Database=${azurerm_mssql_database.main.name};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False;User Id=${var.app_identity_client_id};"
}
