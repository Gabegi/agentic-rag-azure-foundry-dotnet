data "azurerm_client_config" "current" {}

resource "azurerm_key_vault" "main" {
  name                = "kv-support-agent-dev"
  location            = azurerm_resource_group.main.location
  resource_group_name = azurerm_resource_group.main.name
  tenant_id           = data.azurerm_client_config.current.tenant_id
  sku_name            = "standard"

  tags = {
    project     = "support-agent"
    environment = "dev"
  }
}
