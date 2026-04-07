resource "azurerm_search_service" "main" {
  name                = "srch-rag-invoices-dev"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "standard" # Required for semantic ranker + vector search

  semantic_search_sku = "standard" # Enables semantic reranking

  local_authentication_enabled = false # Force Entra ID auth only
  identity {
    type = "SystemAssigned" # Needed to access blob storage
  }

  tags = {
    project     = "support-agent"
    environment = "dev"
  }
}

# Allow AI Search to read from blob storage
resource "azurerm_role_assignment" "search_blob_reader" {
  scope                = azurerm_storage_account.documents.id
  role_definition_name = "Storage Blob Data Reader"
  principal_id         = azurerm_search_service.main.identity[0].principal_id
}
