resource "azurerm_monitor_action_group" "main" {
  name                = "ag-support-agent-dev"
  resource_group_name = azurerm_resource_group.main.name
  short_name          = "searchalert"

  email_receiver {
    name          = "admin"
    email_address = var.alert_email
  }
}

resource "azurerm_monitor_scheduled_query_rules_alert_v2" "throttling" {
  name                = "alert-search-throttling-dev"
  resource_group_name = azurerm_resource_group.main.name
  location            = azurerm_resource_group.main.location

  evaluation_frequency = "PT5M"
  window_duration      = "PT5M"
  scopes               = [azurerm_log_analytics_workspace.main.id]
  severity             = 2

  criteria {
    query = <<-QUERY
      AzureDiagnostics
      | where ResourceProvider == "MICROSOFT.SEARCH"
      | where toint(resultSignature_d) == 503
      | summarize ThrottledRequests = count()
    QUERY

    time_aggregation_method = "Count"
    threshold               = 1
    operator                = "GreaterThanOrEqual"

    failing_periods {
      minimum_failing_periods_to_trigger_alert = 1
      number_of_evaluation_periods             = 1
    }
  }

  action {
    action_groups = [azurerm_monitor_action_group.main.id]
  }

  description = "Fires when any 503 throttling occurs on the search service"
  enabled     = true
}
