variable "name_prefix" { type = string }
variable "suffix" { type = string }
variable "location" { type = string }
variable "aoai_location" { type = string }
variable "resource_group_name" { type = string }
variable "chat_model" { type = string }          # e.g. "gpt-4o-mini:2024-07-18"
variable "embedding_model" { type = string }     # e.g. "text-embedding-3-small:1"
variable "app_identity_principal_id" { type = string }
variable "log_analytics_id" { type = string }
variable "tags" { type = map(string) }

locals {
  chat_parts      = split(":", var.chat_model)
  embedding_parts = split(":", var.embedding_model)
}

# ---------------------------------------------------------------------------
# Azure OpenAI
# Docs: https://learn.microsoft.com/azure/ai-services/openai/how-to/create-resource
# ---------------------------------------------------------------------------
resource "azurerm_cognitive_account" "aoai" {
  name                  = "${var.name_prefix}-aoai-${var.suffix}"
  location              = var.aoai_location
  resource_group_name   = var.resource_group_name
  kind                  = "OpenAI"
  sku_name              = "S0"
  custom_subdomain_name = "${var.name_prefix}-aoai-${var.suffix}"
  tags                  = var.tags
}

resource "azurerm_cognitive_deployment" "chat" {
  name                 = local.chat_parts[0]
  cognitive_account_id = azurerm_cognitive_account.aoai.id
  model {
    format  = "OpenAI"
    name    = local.chat_parts[0]
    version = local.chat_parts[1]
  }
  sku {
    name     = "Standard"
    capacity = 20
  }
}

resource "azurerm_cognitive_deployment" "embedding" {
  name                 = local.embedding_parts[0]
  cognitive_account_id = azurerm_cognitive_account.aoai.id
  model {
    format  = "OpenAI"
    name    = local.embedding_parts[0]
    version = local.embedding_parts[1]
  }
  sku {
    name     = "Standard"
    capacity = 60
  }
}

# App identity gets "Cognitive Services OpenAI User" so we can ditch keys.
resource "azurerm_role_assignment" "aoai_user" {
  scope                = azurerm_cognitive_account.aoai.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = var.app_identity_principal_id
}

# ---------------------------------------------------------------------------
# Azure AI Search
# Docs: https://learn.microsoft.com/azure/search/search-create-service-portal
# ---------------------------------------------------------------------------
resource "azurerm_search_service" "main" {
  name                = "${var.name_prefix}-search-${var.suffix}"
  resource_group_name = var.resource_group_name
  location            = var.location
  sku                 = "basic"
  replica_count       = 1
  partition_count     = 1
  local_authentication_enabled = true  # flip to false once we migrate fully to RBAC
  tags                = var.tags
}

resource "azurerm_role_assignment" "search_data_contributor" {
  scope                = azurerm_search_service.main.id
  role_definition_name = "Search Index Data Contributor"
  principal_id         = var.app_identity_principal_id
}

resource "azurerm_role_assignment" "search_service_contributor" {
  scope                = azurerm_search_service.main.id
  role_definition_name = "Search Service Contributor"
  principal_id         = var.app_identity_principal_id
}

# Diagnostics
resource "azurerm_monitor_diagnostic_setting" "aoai" {
  name                       = "diag-aoai"
  target_resource_id         = azurerm_cognitive_account.aoai.id
  log_analytics_workspace_id = var.log_analytics_id
  enabled_log { category = "Audit" }
  enabled_log { category = "RequestResponse" }
  metric      { category = "AllMetrics" }
}

output "aoai_endpoint"             { value = azurerm_cognitive_account.aoai.endpoint }
output "aoai_chat_deployment"      { value = azurerm_cognitive_deployment.chat.name }
output "aoai_embedding_deployment" { value = azurerm_cognitive_deployment.embedding.name }
output "search_endpoint"           { value = "https://${azurerm_search_service.main.name}.search.windows.net" }
output "search_name"               { value = azurerm_search_service.main.name }
