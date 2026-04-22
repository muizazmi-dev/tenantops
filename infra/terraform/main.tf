resource "azurerm_resource_group" "main" {
  name     = "${local.name_prefix}-rg"
  location = var.location
  tags     = local.base_tags
}

resource "random_string" "suffix" {
  length  = 6
  special = false
  upper   = false
}

# ---------------------------------------------------------------------------
# Observability — Log Analytics + App Insights. Every module pipes here.
# Docs: https://learn.microsoft.com/azure/azure-monitor/logs/log-analytics-workspace-overview
# ---------------------------------------------------------------------------
resource "azurerm_log_analytics_workspace" "main" {
  name                = "${local.name_prefix}-law"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "PerGB2018"
  retention_in_days   = 30
  tags                = local.base_tags
}

resource "azurerm_application_insights" "main" {
  name                = "${local.name_prefix}-ai"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  application_type    = "web"
  workspace_id        = azurerm_log_analytics_workspace.main.id
  tags                = local.base_tags
}

# ---------------------------------------------------------------------------
# Container Registry
# ---------------------------------------------------------------------------
resource "azurerm_container_registry" "main" {
  name                = "${replace(local.name_prefix, "-", "")}acr${random_string.suffix.result}"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  sku                 = "Standard"
  admin_enabled       = false
  tags                = local.base_tags
}

# User-assigned managed identity used by Container Apps to pull from ACR
# and authenticate to Azure SQL / OpenAI / Search.
# Docs: https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview
resource "azurerm_user_assigned_identity" "apps" {
  name                = "${local.name_prefix}-apps-mi"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tags                = local.base_tags
}

resource "azurerm_role_assignment" "acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = azurerm_user_assigned_identity.apps.principal_id
}

# ---------------------------------------------------------------------------
# Data layer — Azure SQL (module)
# ---------------------------------------------------------------------------
module "sql" {
  source              = "./modules/sql"
  name_prefix         = local.name_prefix
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  admin_login         = var.sql_admin_login
  admin_password      = var.sql_admin_password
  sku                 = var.sql_sku
  app_identity_object_id = azurerm_user_assigned_identity.apps.principal_id
  app_identity_client_id = azurerm_user_assigned_identity.apps.client_id
  app_identity_name      = azurerm_user_assigned_identity.apps.name
  log_analytics_id    = azurerm_log_analytics_workspace.main.id
  tags                = local.base_tags
}

# ---------------------------------------------------------------------------
# AI layer — Azure OpenAI + AI Search (module)
# ---------------------------------------------------------------------------
module "ai" {
  source                = "./modules/ai"
  name_prefix           = local.name_prefix
  suffix                = random_string.suffix.result
  aoai_location         = var.aoai_location
  resource_group_name   = azurerm_resource_group.main.name
  location              = azurerm_resource_group.main.location
  chat_model            = var.aoai_chat_model
  embedding_model       = var.aoai_embedding_model
  app_identity_principal_id = azurerm_user_assigned_identity.apps.principal_id
  log_analytics_id      = azurerm_log_analytics_workspace.main.id
  tags                  = local.base_tags
}

# ---------------------------------------------------------------------------
# Application layer — Container Apps environment + 4 apps + Dapr (module)
# ---------------------------------------------------------------------------
module "aca" {
  source              = "./modules/aca"
  name_prefix         = local.name_prefix
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  acr_login_server    = azurerm_container_registry.main.login_server
  identity_id         = azurerm_user_assigned_identity.apps.id
  identity_client_id  = azurerm_user_assigned_identity.apps.client_id
  log_analytics_id    = azurerm_log_analytics_workspace.main.id
  app_insights_connection_string = azurerm_application_insights.main.connection_string

  sql_connection_string = module.sql.connection_string_for_managed_identity
  aoai_endpoint         = module.ai.aoai_endpoint
  aoai_chat_deployment  = module.ai.aoai_chat_deployment
  aoai_embed_deployment = module.ai.aoai_embedding_deployment
  search_endpoint       = module.ai.search_endpoint
  search_index          = "tenantops-kb"

  entra_tenant_id = var.entra_tenant_id
  entra_audience  = var.entra_audience

  tags = local.base_tags
}

# ---------------------------------------------------------------------------
# Edge — API Management gateway (module)
# ---------------------------------------------------------------------------
module "apim" {
  source              = "./modules/apim"
  name_prefix         = local.name_prefix
  suffix              = random_string.suffix.result
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name

  tenant_api_backend = module.aca.tenant_api_fqdn
  core_api_backend   = module.aca.core_api_fqdn
  ai_api_backend     = module.aca.ai_api_fqdn
  identity_backend   = module.aca.identity_api_fqdn

  entra_tenant_id  = var.entra_tenant_id
  entra_audience   = var.entra_audience
  local_jwt_issuer = "https://tenantops.local/identity"

  log_analytics_id = azurerm_log_analytics_workspace.main.id
  app_insights_id  = azurerm_application_insights.main.id
  tags             = local.base_tags
}

# ---------------------------------------------------------------------------
# Edge — Azure Front Door + WAF (module)
# ---------------------------------------------------------------------------
module "frontdoor" {
  source              = "./modules/frontdoor"
  name_prefix         = local.name_prefix
  resource_group_name = azurerm_resource_group.main.name
  web_origin_hostname = module.aca.web_fqdn
  apim_origin_hostname = module.apim.gateway_hostname
  custom_domains      = var.custom_domains
  tags                = local.base_tags
}
