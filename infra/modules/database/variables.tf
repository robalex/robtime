variable "environment" {
  description = "Environment name (e.g. \"staging\"), used in resource names/tags."
  type        = string
}

variable "private_subnet_ids" {
  description = "From the network module's output — RDS lives here, never in a public subnet."
  type        = list(string)
}

variable "security_group_id" {
  description = "From the network module's output — allows inbound 5432 from the VPC Connector's security group only."
  type        = string
}

variable "instance_class" {
  description = "DEPLOY_PLAN.md §2 defaults staging to db.t4g.small (burstable, cheap)."
  type        = string
  default     = "db.t4g.small"
}

variable "apply_immediately" {
  description = "Whether RDS changes apply immediately or wait for the next maintenance window. true for staging (fast iteration); production should default false."
  type        = bool
  default     = true
}
