resource "azapi_resource" "invoice_index" {
  type                    = "Microsoft.Search/searchServices/indexes@2025-05-01"
  name                    = "invoices"
  parent_id               = azurerm_search_service.main.id
  schema_validation_enabled = false

  body = {
    properties = {
      fields = [
        { name = "id",            type = "Edm.String",         key = true,  searchable = false },
        { name = "vendor",        type = "Edm.String",         searchable = true, filterable = true, facetable = true },
        { name = "amount",        type = "Edm.Double",         searchable = false, filterable = true, sortable = true },
        { name = "discount",      type = "Edm.Double",         searchable = false, filterable = true },
        { name = "category",      type = "Edm.String",         searchable = true, filterable = true, facetable = true },
        { name = "date",          type = "Edm.DateTimeOffset", filterable = true, sortable = true },
        { name = "payment_terms", type = "Edm.String",         searchable = true, filterable = true },
        { name = "content",       type = "Edm.String",         searchable = true },
        { name = "content_vector", type = "Collection(Edm.Single)", searchable = true, dimensions = 1536, vectorSearchProfile = "vector-profile" }
      ]
      vectorSearch = {
        profiles   = [{ name = "vector-profile", algorithmConfigurationName = "hnsw-config" }]
        algorithms = [{ name = "hnsw-config", kind = "hnsw" }]
      }
      semanticSearch = {
        configurations = [{
          name = "semantic-config"
          prioritizedFields = {
            contentFields  = [{ fieldName = "content" }]
            keywordsFields = [{ fieldName = "vendor" }, { fieldName = "category" }]
          }
        }]
      }
    }
  }
}
