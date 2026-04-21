data "azurerm_virtual_machine" "runner" {
  name                = "vm-github-runner"
  resource_group_name = "rg-github-runner"
}

resource "azurerm_container_registry" "main" {
  name                = "crragtest"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  sku                 = "Basic"
  admin_enabled       = false # Use managed identity, not admin credentials

  tags = {
    project     = "support-agent"
    environment = "dev"
  }
}

# Allow ACI to pull images from ACR
resource "azurerm_role_assignment" "mi_acr_pull" {
  scope                = azurerm_container_registry.main.id
  role_definition_name = "AcrPull"
  principal_id         = data.azurerm_virtual_machine.runner.identity[0].principal_id
}