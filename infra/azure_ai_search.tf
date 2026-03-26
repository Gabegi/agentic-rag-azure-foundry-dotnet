resource "azurerm_search_service" "main" {
  name                = "srch-support-agent-dev"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "basic"

  tags = {
    project     = "support-agent"
    environment = "dev"
  }
}
