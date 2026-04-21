resource "azurerm_cognitive_account" "openai" {
  name                = "oai-support-agent-dev"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location
  kind                = "OpenAI"
  sku_name            = "S0"

  tags = {
    project     = "support-agent"
    environment = "dev"
  }
}

resource "azurerm_cognitive_deployment" "embedding" {
  name                 = var.openai_embedding_deployment
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = "text-embedding-3-large"
    version = "1"
  }

  sku {
    name     = "Standard"
    capacity = 50
  }
}

resource "azurerm_cognitive_deployment" "querying" {
  name                 = var.openai_gpt_deployment
  cognitive_account_id = azurerm_cognitive_account.openai.id

  model {
    format  = "OpenAI"
    name    = var.openai_gpt_model_name
    version = "2025-01-01-preview"
  }

  sku {
    name     = "Standard"
    capacity = 10
  }
}

resource "azurerm_role_assignment" "sp_openai_user" {
  scope                = azurerm_cognitive_account.openai.id
  role_definition_name = "Cognitive Services OpenAI User"
  principal_id         = data.azurerm_client_config.current.object_id
}
