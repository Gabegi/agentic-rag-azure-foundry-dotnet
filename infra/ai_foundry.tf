# resource "azurerm_storage_account" "main" {
#   name                     = "stsupportagentdev"
#   resource_group_name      = azurerm_resource_group.main.name
#   location                 = azurerm_resource_group.main.location
#   account_tier             = "Standard"
#   account_replication_type = "LRS"

#   tags = {
#     project     = "support-agent"
#     environment = "dev"
#   }
# }

# resource "azurerm_ai_foundry" "main" {
#   name                = "aif-support-agent-dev"
#   location            = azurerm_resource_group.main.location
#   resource_group_name = azurerm_resource_group.main.name
#   storage_account_id  = azurerm_storage_account.main.id
#   key_vault_id        = azurerm_key_vault.main.id

#   identity {
#     type = "SystemAssigned"
#   }

#   tags = {
#     project     = "support-agent"
#     environment = "dev"
#   }
# }
