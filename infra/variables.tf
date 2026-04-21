variable "subscription_id" {
  description = "Azure subscription ID to deploy resources into"
  type        = string
}

variable "openai_embedding_deployment" {
  description = "Name of the Azure OpenAI embedding deployment"
  type        = string
  default     = "text-embedding-3-large"
}

variable "openai_gpt_deployment" {
  description = "Name of the Azure OpenAI GPT deployment"
  type        = string
  default     = "gpt-4o-mini"
}

variable "openai_gpt_model_name" {
  description = "Model name for the Azure OpenAI GPT deployment"
  type        = string
  default     = "gpt-4o-mini"
}
