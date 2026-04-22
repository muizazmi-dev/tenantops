variable "name_prefix" { type = string }
variable "suffix" { type = string }
variable "location" { type = string }
variable "resource_group_name" { type = string }
variable "tenant_api_backend" { type = string }
variable "core_api_backend" { type = string }
variable "ai_api_backend" { type = string }
variable "identity_backend" { type = string }
variable "entra_tenant_id" { type = string }
variable "entra_audience" { type = string }
variable "local_jwt_issuer" { type = string }
variable "log_analytics_id" { type = string }
variable "app_insights_id" { type = string }
variable "tags" { type = map(string) }

# ---------------------------------------------------------------------------
# API Management instance (Developer SKU for demo; upgrade for prod).
# Docs: https://learn.microsoft.com/azure/api-management/api-management-key-concepts
# ---------------------------------------------------------------------------
resource "azurerm_api_management" "main" {
  name                = "${var.name_prefix}-apim-${var.suffix}"
  location            = var.location
  resource_group_name = var.resource_group_name
  publisher_name      = "TenantOps Platform"
  publisher_email     = "platform@tenantops.test"
  sku_name            = "Developer_1"
  identity { type = "SystemAssigned" }
  tags                = var.tags
}

# Hook App Insights for APIM request telemetry
resource "azurerm_api_management_logger" "appinsights" {
  name                = "appinsights"
  api_management_name = azurerm_api_management.main.name
  resource_group_name = var.resource_group_name
  application_insights { instrumentation_key = data.azurerm_application_insights.ref.instrumentation_key }
}

data "azurerm_application_insights" "ref" {
  name                = split("/", var.app_insights_id)[8]
  resource_group_name = split("/", var.app_insights_id)[4]
}

# ---------------------------------------------------------------------------
# Policy templating — we render the on-disk XML with token substitution
# (entra_tenant_id, entra_audience, local_jwt_issuer, frontdoor_endpoint).
# ---------------------------------------------------------------------------
locals {
  policy_global = templatefile("${path.root}/../apim/policies/global.xml", {
    frontdoor_endpoint = "${var.name_prefix}-fd.azurefd.net"
  })

  policy_auth = templatefile("${path.root}/../apim/policies/api-authenticated.xml", {
    entra_tenant_id  = var.entra_tenant_id
    entra_audience   = var.entra_audience
    local_jwt_issuer = var.local_jwt_issuer
  })

  policy_public = file("${path.root}/../apim/policies/api-resolve-public.xml")
}

resource "azurerm_api_management_policy" "global" {
  api_management_id = azurerm_api_management.main.id
  xml_content       = local.policy_global
}

# ---------------------------------------------------------------------------
# Backends — one per ACA app.
# ---------------------------------------------------------------------------
resource "azurerm_api_management_backend" "tenant" {
  name                = "tenant-api-be"
  resource_group_name = var.resource_group_name
  api_management_name = azurerm_api_management.main.name
  protocol            = "http"
  url                 = "https://${var.tenant_api_backend}"
}
resource "azurerm_api_management_backend" "core" {
  name                = "core-api-be"
  resource_group_name = var.resource_group_name
  api_management_name = azurerm_api_management.main.name
  protocol            = "http"
  url                 = "https://${var.core_api_backend}"
}
resource "azurerm_api_management_backend" "ai" {
  name                = "ai-api-be"
  resource_group_name = var.resource_group_name
  api_management_name = azurerm_api_management.main.name
  protocol            = "http"
  url                 = "https://${var.ai_api_backend}"
}
resource "azurerm_api_management_backend" "identity" {
  name                = "identity-be"
  resource_group_name = var.resource_group_name
  api_management_name = azurerm_api_management.main.name
  protocol            = "http"
  url                 = "https://${var.identity_backend}"
}

# ---------------------------------------------------------------------------
# APIs
# ---------------------------------------------------------------------------
resource "azurerm_api_management_api" "tenant" {
  name                = "tenant-api"
  resource_group_name = var.resource_group_name
  api_management_name = azurerm_api_management.main.name
  revision            = "1"
  display_name        = "Tenant API"
  path                = "tenant"
  protocols           = ["https"]
  service_url         = "https://${var.tenant_api_backend}"
  subscription_required = true
}

# /resolve is public; everything else on tenant-api follows the auth policy
resource "azurerm_api_management_api_policy" "tenant_auth" {
  api_name            = azurerm_api_management_api.tenant.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = var.resource_group_name
  xml_content         = local.policy_auth
}

resource "azurerm_api_management_api_operation" "tenant_resolve" {
  operation_id        = "resolve"
  api_name            = azurerm_api_management_api.tenant.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = var.resource_group_name
  display_name        = "Resolve tenant"
  method              = "GET"
  url_template        = "/resolve"
  response { status_code = 200 }
}

resource "azurerm_api_management_api_operation_policy" "tenant_resolve" {
  api_name            = azurerm_api_management_api.tenant.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = var.resource_group_name
  operation_id        = azurerm_api_management_api_operation.tenant_resolve.operation_id
  xml_content         = local.policy_public   # overrides API-level auth for this op
}

resource "azurerm_api_management_api" "core" {
  name                  = "core-api"
  resource_group_name   = var.resource_group_name
  api_management_name   = azurerm_api_management.main.name
  revision              = "1"
  display_name          = "Core API"
  path                  = "core"
  protocols             = ["https"]
  service_url           = "https://${var.core_api_backend}"
  subscription_required = true
}

resource "azurerm_api_management_api_policy" "core_auth" {
  api_name            = azurerm_api_management_api.core.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = var.resource_group_name
  xml_content         = local.policy_auth
}

resource "azurerm_api_management_api" "ai" {
  name                  = "ai-api"
  resource_group_name   = var.resource_group_name
  api_management_name   = azurerm_api_management.main.name
  revision              = "1"
  display_name          = "AI Orchestrator"
  path                  = "ai"
  protocols             = ["https"]
  service_url           = "https://${var.ai_api_backend}"
  subscription_required = true
}

resource "azurerm_api_management_api_policy" "ai_auth" {
  api_name            = azurerm_api_management_api.ai.name
  api_management_name = azurerm_api_management.main.name
  resource_group_name = var.resource_group_name
  xml_content         = local.policy_auth
}

output "gateway_hostname" { value = azurerm_api_management.main.gateway_url }
