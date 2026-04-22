variable "name_prefix" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "acr_login_server" { type = string }
variable "identity_id" { type = string }
variable "identity_client_id" { type = string }
variable "log_analytics_id" { type = string }
variable "app_insights_connection_string" { type = string, sensitive = true }
variable "sql_connection_string" { type = string, sensitive = true }
variable "aoai_endpoint" { type = string }
variable "aoai_chat_deployment" { type = string }
variable "aoai_embed_deployment" { type = string }
variable "search_endpoint" { type = string }
variable "search_index" { type = string }
variable "entra_tenant_id" { type = string }
variable "entra_audience" { type = string }
variable "tags" { type = map(string) }

data "azurerm_log_analytics_workspace" "this" {
  name                = split("/", var.log_analytics_id)[8]
  resource_group_name = split("/", var.log_analytics_id)[4]
}

# ---------------------------------------------------------------------------
# Container Apps environment (Dapr enabled)
# Docs: https://learn.microsoft.com/azure/container-apps/environment
#       https://learn.microsoft.com/azure/container-apps/dapr-overview
# ---------------------------------------------------------------------------
resource "azurerm_container_app_environment" "main" {
  name                       = "${var.name_prefix}-cae"
  location                   = var.location
  resource_group_name        = var.resource_group_name
  log_analytics_workspace_id = var.log_analytics_id
  tags                       = var.tags
}

# -------------------- helpers ---------------------------------------------
locals {
  common_env = [
    { name = "ASPNETCORE_URLS",                    value = "http://+:8080" },
    { name = "ConnectionStrings__Default",         value = var.sql_connection_string },
    { name = "Entra__Issuer",                      value = "https://login.microsoftonline.com/${var.entra_tenant_id}/v2.0" },
    { name = "Entra__Audience",                    value = var.entra_audience },
    { name = "APPLICATIONINSIGHTS_CONNECTION_STRING", value = var.app_insights_connection_string },
    { name = "AZURE_CLIENT_ID",                    value = var.identity_client_id }
  ]
}

# -------------------- identity-api ----------------------------------------
resource "azurerm_container_app" "identity" {
  name                         = "${var.name_prefix}-identity-api"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  tags                         = var.tags

  identity {
    type         = "UserAssigned"
    identity_ids = [var.identity_id]
  }

  registry {
    server   = var.acr_login_server
    identity = var.identity_id
  }

  template {
    container {
      name   = "identity-api"
      image  = "${var.acr_login_server}/tenantops/identity-api:latest"
      cpu    = 0.25
      memory = "0.5Gi"
      dynamic "env" { for_each = local.common_env; content { name = env.value.name; value = env.value.value } }
    }
    min_replicas = 1
    max_replicas = 3
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    traffic_weight { latest_revision = true; percentage = 100 }
  }

  dapr {
    app_id       = "identity-api"
    app_port     = 8080
    app_protocol = "http"
  }
}

# -------------------- tenant-api ------------------------------------------
resource "azurerm_container_app" "tenant" {
  name                         = "${var.name_prefix}-tenant-api"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  tags                         = var.tags

  identity { type = "UserAssigned"; identity_ids = [var.identity_id] }
  registry { server = var.acr_login_server; identity = var.identity_id }

  template {
    container {
      name   = "tenant-api"
      image  = "${var.acr_login_server}/tenantops/tenant-api:latest"
      cpu    = 0.25
      memory = "0.5Gi"
      dynamic "env" { for_each = local.common_env; content { name = env.value.name; value = env.value.value } }
    }
    min_replicas = 1
    max_replicas = 3
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    traffic_weight { latest_revision = true; percentage = 100 }
  }

  dapr { app_id = "tenant-api"; app_port = 8080; app_protocol = "http" }
}

# -------------------- core-api --------------------------------------------
resource "azurerm_container_app" "core" {
  name                         = "${var.name_prefix}-core-api"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  tags                         = var.tags

  identity { type = "UserAssigned"; identity_ids = [var.identity_id] }
  registry { server = var.acr_login_server; identity = var.identity_id }

  template {
    container {
      name   = "core-api"
      image  = "${var.acr_login_server}/tenantops/core-api:latest"
      cpu    = 0.5
      memory = "1Gi"
      dynamic "env" { for_each = local.common_env; content { name = env.value.name; value = env.value.value } }
    }
    min_replicas = 1
    max_replicas = 5
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    traffic_weight { latest_revision = true; percentage = 100 }
  }

  dapr { app_id = "core-api"; app_port = 8080; app_protocol = "http" }
}

# -------------------- ai-orchestrator -------------------------------------
resource "azurerm_container_app" "ai" {
  name                         = "${var.name_prefix}-ai-orchestrator"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  tags                         = var.tags

  identity { type = "UserAssigned"; identity_ids = [var.identity_id] }
  registry { server = var.acr_login_server; identity = var.identity_id }

  template {
    container {
      name   = "ai-orchestrator"
      image  = "${var.acr_login_server}/tenantops/ai-orchestrator:latest"
      cpu    = 0.5
      memory = "1Gi"
      dynamic "env" { for_each = local.common_env; content { name = env.value.name; value = env.value.value } }
      env { name = "AzureOpenAI__Endpoint";              value = var.aoai_endpoint }
      env { name = "AzureOpenAI__ChatDeployment";        value = var.aoai_chat_deployment }
      env { name = "AzureOpenAI__EmbeddingDeployment";   value = var.aoai_embed_deployment }
      env { name = "AzureOpenAI__ApiVersion";            value = "2024-10-21" }
      env { name = "AzureSearch__Endpoint";              value = var.search_endpoint }
      env { name = "AzureSearch__IndexName";             value = var.search_index }
      env { name = "AzureSearch__ApiVersion";            value = "2025-09-01" }
    }
    min_replicas = 1
    max_replicas = 3
  }

  ingress {
    external_enabled = false
    target_port      = 8080
    traffic_weight { latest_revision = true; percentage = 100 }
  }

  dapr { app_id = "ai-orchestrator"; app_port = 8080; app_protocol = "http" }
}

# -------------------- web (Next.js, external ingress) ---------------------
resource "azurerm_container_app" "web" {
  name                         = "${var.name_prefix}-web"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = var.resource_group_name
  revision_mode                = "Single"
  tags                         = var.tags

  identity { type = "UserAssigned"; identity_ids = [var.identity_id] }
  registry { server = var.acr_login_server; identity = var.identity_id }

  template {
    container {
      name   = "web"
      image  = "${var.acr_login_server}/tenantops/web:latest"
      cpu    = 0.5
      memory = "1Gi"
      env { name = "NODE_ENV";                value = "production" }
      env { name = "TENANT_API_BASE_URL";     value = "https://${azurerm_container_app.tenant.ingress[0].fqdn}" }
      env { name = "CORE_API_BASE_URL";       value = "https://${azurerm_container_app.core.ingress[0].fqdn}" }
      env { name = "AI_API_BASE_URL";         value = "https://${azurerm_container_app.ai.ingress[0].fqdn}" }
      env { name = "IDENTITY_API_BASE_URL";   value = "https://${azurerm_container_app.identity.ingress[0].fqdn}" }
      env { name = "NEXT_PUBLIC_APP_NAME";    value = "TenantOps" }
    }
    min_replicas = 1
    max_replicas = 5
  }

  ingress {
    external_enabled = true
    target_port      = 3000
    traffic_weight { latest_revision = true; percentage = 100 }
  }
}

output "identity_api_fqdn" { value = azurerm_container_app.identity.ingress[0].fqdn }
output "tenant_api_fqdn"   { value = azurerm_container_app.tenant.ingress[0].fqdn }
output "core_api_fqdn"     { value = azurerm_container_app.core.ingress[0].fqdn }
output "ai_api_fqdn"       { value = azurerm_container_app.ai.ingress[0].fqdn }
output "web_fqdn"          { value = azurerm_container_app.web.ingress[0].fqdn }
