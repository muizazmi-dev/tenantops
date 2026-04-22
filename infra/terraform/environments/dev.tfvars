project         = "tenantops"
environment     = "dev"
location        = "southeastasia"
aoai_location   = "eastus"
sql_sku         = "S0"
entra_tenant_id = "00000000-0000-0000-0000-000000000000"   # replace
entra_audience  = "api://tenantops"
# sql_admin_password is injected via TF_VAR_sql_admin_password or a secret store
custom_domains  = []
tags = {
  owner = "platform"
}
