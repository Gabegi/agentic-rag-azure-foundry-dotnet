resource "azurerm_search_service" "main" {
  name                = "srch-${var.project_name}-${var.environment}"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "basic"

  tags = {
    project     = var.project_name
    environment = var.environment
  }
}
