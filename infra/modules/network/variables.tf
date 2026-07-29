variable "environment" {
  description = "Environment name (e.g. \"staging\"), used in resource names/tags."
  type        = string
}

variable "vpc_cidr" {
  description = "CIDR block for the VPC. /16 split into /24 subnets (2 public, 2 private) via cidrsubnet()."
  type        = string
  default     = "10.0.0.0/16"
}
