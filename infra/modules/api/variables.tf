variable "environment" {
  description = "Environment name (e.g. \"staging\"), used in resource names/tags and as ASPNETCORE_ENVIRONMENT (title-cased)."
  type        = string
}

variable "aws_region" {
  description = "Region App Runner and the Cognito SDK client run in — passed through as the Cognito__Region runtime env var."
  type        = string
}

variable "private_subnet_ids" {
  description = "From the network module — the VPC Connector's ENIs live here."
  type        = list(string)
}

variable "vpc_connector_security_group_id" {
  description = "From the network module — attached to the VPC Connector so it's the one allowed inbound to the database security group."
  type        = string
}

variable "db_endpoint" {
  description = "From the database module's `endpoint` output (\"host:port\") — split here into Database__Host/Database__Port runtime env vars."
  type        = string
}

variable "db_name" {
  type = string
}

variable "db_username" {
  type = string
}

variable "db_master_user_secret_arn" {
  description = "From the database module — the instance role gets secretsmanager:GetSecretValue on this ARN so App Runner can resolve Database__Password at deploy time (RuntimeEnvironmentSecrets), never through Terraform."
  type        = string
}

variable "cognito_user_pool_id" {
  type = string
}

variable "cognito_user_pool_client_id" {
  type = string
}

variable "cognito_user_pool_arn" {
  description = "Scopes the instance role's cognito-idp:Admin* permissions to this pool only."
  type        = string
}

variable "cpu" {
  description = "App Runner instance CPU (e.g. \"0.25 vCPU\"). Staging defaults to the smallest tier."
  type        = string
  default     = "0.25 vCPU"
}

variable "memory" {
  description = "App Runner instance memory (e.g. \"0.5 GB\"). Staging defaults to the smallest tier."
  type        = string
  default     = "0.5 GB"
}

variable "image_tag" {
  description = "ECR image tag App Runner deploys. auto_deployments_enabled watches this tag for new pushes (CI builds and pushes here — DEPLOY_PLAN.md's CI/CD row)."
  type        = string
  default     = "latest"
}
