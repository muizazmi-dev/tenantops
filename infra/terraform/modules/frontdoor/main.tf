variable "name_prefix" { type = string }
variable "resource_group_name" { type = string }
variable "web_origin_hostname" { type = string }
variable "apim_origin_hostname" { type = string }
variable "custom_domains" { type = list(string) }
variable "tags" { type = map(string) }

# ---------------------------------------------------------------------------
# WAF policy (Premium required for managed rules; using Standard + custom for demo)
# Docs: https://learn.microsoft.com/azure/web-application-firewall/afds/afds-overview
# ---------------------------------------------------------------------------
resource "azurerm_cdn_frontdoor_firewall_policy" "main" {
  name                              = replace("${var.name_prefix}waf", "-", "")
  resource_group_name               = var.resource_group_name
  sku_name                          = "Premium_AzureFrontDoor"
  enabled                           = true
  mode                              = "Prevention"
  custom_block_response_status_code = 403

  managed_rule {
    type    = "Microsoft_DefaultRuleSet"
    version = "2.1"
    action  = "Block"
  }
  managed_rule {
    type    = "Microsoft_BotManagerRuleSet"
    version = "1.1"
    action  = "Block"
  }

  custom_rule {
    name                           = "BlockHighRpsPerIp"
    enabled                        = true
    priority                       = 100
    rate_limit_duration_in_minutes = 1
    rate_limit_threshold           = 600
    type                           = "RateLimitRule"
    action                         = "Block"
    match_condition {
      match_variable     = "RemoteAddr"
      operator           = "IPMatch"
      negation_condition = false
      match_values       = ["0.0.0.0/0"]
    }
  }

  tags = var.tags
}

resource "azurerm_cdn_frontdoor_profile" "main" {
  name                = "${var.name_prefix}-fd"
  resource_group_name = var.resource_group_name
  sku_name            = "Premium_AzureFrontDoor"
  tags                = var.tags
}

resource "azurerm_cdn_frontdoor_endpoint" "main" {
  name                     = "${var.name_prefix}-ep"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.main.id
  tags                     = var.tags
}

# Origin groups --------------------------------------------------------------
resource "azurerm_cdn_frontdoor_origin_group" "web" {
  name                     = "web-og"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.main.id
  load_balancing { sample_size = 4; successful_samples_required = 3 }
  health_probe {
    protocol            = "Https"
    interval_in_seconds = 30
    path                = "/"
    request_type        = "HEAD"
  }
}

resource "azurerm_cdn_frontdoor_origin" "web" {
  name                          = "web-origin"
  cdn_frontdoor_origin_group_id = azurerm_cdn_frontdoor_origin_group.web.id
  enabled                       = true
  host_name                     = var.web_origin_hostname
  http_port                     = 80
  https_port                    = 443
  origin_host_header            = var.web_origin_hostname
  priority                      = 1
  weight                        = 1000
  certificate_name_check_enabled = true
}

resource "azurerm_cdn_frontdoor_origin_group" "apim" {
  name                     = "apim-og"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.main.id
  load_balancing { sample_size = 4; successful_samples_required = 3 }
  health_probe {
    protocol            = "Https"
    interval_in_seconds = 30
    path                = "/status-0123456789abcdef"
    request_type        = "GET"
  }
}

resource "azurerm_cdn_frontdoor_origin" "apim" {
  name                          = "apim-origin"
  cdn_frontdoor_origin_group_id = azurerm_cdn_frontdoor_origin_group.apim.id
  enabled                       = true
  host_name                     = replace(replace(var.apim_origin_hostname, "https://", ""), "/", "")
  http_port                     = 80
  https_port                    = 443
  origin_host_header            = replace(replace(var.apim_origin_hostname, "https://", ""), "/", "")
  priority                      = 1
  weight                        = 1000
  certificate_name_check_enabled = true
}

# Routes ---------------------------------------------------------------------
resource "azurerm_cdn_frontdoor_route" "web" {
  name                          = "web-route"
  cdn_frontdoor_endpoint_id     = azurerm_cdn_frontdoor_endpoint.main.id
  cdn_frontdoor_origin_group_id = azurerm_cdn_frontdoor_origin_group.web.id
  cdn_frontdoor_origin_ids      = [azurerm_cdn_frontdoor_origin.web.id]
  supported_protocols           = ["Http", "Https"]
  patterns_to_match             = ["/*"]
  forwarding_protocol           = "HttpsOnly"
  link_to_default_domain        = true
  https_redirect_enabled        = true
}

resource "azurerm_cdn_frontdoor_route" "api" {
  name                          = "api-route"
  cdn_frontdoor_endpoint_id     = azurerm_cdn_frontdoor_endpoint.main.id
  cdn_frontdoor_origin_group_id = azurerm_cdn_frontdoor_origin_group.apim.id
  cdn_frontdoor_origin_ids      = [azurerm_cdn_frontdoor_origin.apim.id]
  supported_protocols           = ["Https"]
  patterns_to_match             = ["/api/*"]
  forwarding_protocol           = "HttpsOnly"
  link_to_default_domain        = true
  https_redirect_enabled        = true
}

# WAF security policy attachment --------------------------------------------
resource "azurerm_cdn_frontdoor_security_policy" "main" {
  name                     = "${var.name_prefix}-secpolicy"
  cdn_frontdoor_profile_id = azurerm_cdn_frontdoor_profile.main.id
  security_policies {
    firewall {
      cdn_frontdoor_firewall_policy_id = azurerm_cdn_frontdoor_firewall_policy.main.id
      association {
        domain { cdn_frontdoor_domain_id = azurerm_cdn_frontdoor_endpoint.main.id }
        patterns_to_match = ["/*"]
      }
    }
  }
}

output "endpoint_hostname" { value = azurerm_cdn_frontdoor_endpoint.main.host_name }
