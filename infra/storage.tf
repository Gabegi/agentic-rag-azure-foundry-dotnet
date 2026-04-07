# storage.tf
# Storage account and blob container for RAG document ingestion

resource "azurerm_storage_account" "documents" {
  name                     = "stinvdocsdev"
  resource_group_name      = azurerm_resource_group.main.name
  location                 = azurerm_resource_group.main.location
  account_tier             = "Standard"
  account_replication_type = "LRS"

  tags = {
    project     = "support-agent"
    environment = "dev"
  }
}

resource "azurerm_storage_container" "documents" {
  name                  = "documents"
  storage_account_id    = azurerm_storage_account.documents.id
  container_access_type = "private"
}
