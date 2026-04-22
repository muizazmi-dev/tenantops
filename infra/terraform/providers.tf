terraform {
  required_version = ">= 1.6.0"

  required_providers {
    azurerm = { source = "hashicorp/azurerm", version = "~> 4.10" }
    azuread = { source = "hashicorp/azuread", version = "~> 3.0" }
    random  = { source = "hashicorp/random",  version = "~> 3.6" }
  }

  # Remote state. Populate via -backend-config=backends/<env>.tfvars
  # Docs: https://learn.microsoft.com/azure/developer/terraform/store-state-in-azure-storage
  backend "azurerm" {}
}

provider "azurerm" {
  features {
    key_vault { purge_soft_delete_on_destroy = true }
    resource_group { prevent_deletion_if_contains_resources = false }
  }
}

provider "azuread" {}
