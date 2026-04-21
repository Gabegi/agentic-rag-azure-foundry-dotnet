resource "azurerm_cognitive_account" "document_intelligence" {
  name                = "di-support-agent-dev"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  kind                = "FormRecognizer"
  sku_name            = "S0"

  tags = {
    project     = "support-agent"
    environment = "dev"
  }
}

resource "azurerm_role_assignment" "sp_di_user" {
  scope                = azurerm_cognitive_account.document_intelligence.id
  role_definition_name = "Cognitive Services User"
  principal_id         = data.azurerm_client_config.current.object_id
}
