variable "project" {
  description = "Short project slug (used as resource-name prefix)."
  type        = string
  default     = "tenantops"
}

variable "environment" {
  description = "dev | staging | prod"
  type        = string
  default     = "dev"
}

variable "location" {
  description = "Primary Azure region."
  type        = string
  default     = "southeastasia"
}

variable "tags" {
  description = "Tags applied to every resource."
  type        = map(string)
  default     = {}
}

# ---- SQL ------------------------------------------------------------------
variable "sql_admin_login" {
  description = "SQL admin login (used only for bootstrap; app users use managed identity)."
  type        = string
  default     = "tenantops_sa"
}

variable "sql_admin_password" {
  description = "SQL admin password. Supply via TF_VAR_sql_admin_password or a tfvars file."
  type        = string
  sensitive   = true
}

variable "sql_sku" {
  description = "Azure SQL database SKU."
  type        = string
  default     = "S0"
}

# ---- Azure OpenAI ---------------------------------------------------------
variable "aoai_location" {
  description = "Azure region for the OpenAI resource (model availability varies)."
  type        = string
  default     = "eastus"
}

variable "aoai_chat_model" {
  description = "Chat model deployment (name:version)."
  type        = string
  default     = "gpt-4o-mini:2024-07-18"
}

variable "aoai_embedding_model" {
  description = "Embedding model deployment (name:version)."
  type        = string
  default     = "text-embedding-3-small:1"
}

# ---- Entra ----------------------------------------------------------------
variable "entra_audience" {
  description = "API audience (Entra app ID URI) used by validate-jwt."
  type        = string
  default     = "api://tenantops"
}

variable "entra_tenant_id" {
  description = "Microsoft Entra tenant id."
  type        = string
}

# ---- Front Door -----------------------------------------------------------
variable "custom_domains" {
  description = "Optional custom domains to attach to Front Door."
  type        = list(string)
  default     = []
}

locals {
  name_prefix = "${var.project}-${var.environment}"
  base_tags = merge({
    project     = var.project
    environment = var.environment
    managed-by  = "terraform"
  }, var.tags)
}
