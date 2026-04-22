output "resource_group" {
  value = azurerm_resource_group.main.name
}

output "front_door_hostname" {
  value       = module.frontdoor.endpoint_hostname
  description = "Public entrypoint. Point DNS here."
}

output "apim_gateway" {
  value = module.apim.gateway_hostname
}

output "web_fqdn" {
  value = module.aca.web_fqdn
}

output "acr_login_server" {
  value = azurerm_container_registry.main.login_server
}

output "sql_server_fqdn" {
  value = module.sql.server_fqdn
}

output "aoai_endpoint" {
  value     = module.ai.aoai_endpoint
  sensitive = true
}

output "search_endpoint" {
  value = module.ai.search_endpoint
}

output "app_insights_connection_string" {
  value     = azurerm_application_insights.main.connection_string
  sensitive = true
}
