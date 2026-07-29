variable "environment" {
  description = "Environment name (e.g. \"staging\"), used in resource names/tags."
  type        = string
}

variable "api_domain_name" {
  description = "The api module's `service_url` output (App Runner's default *.awsapprunner.com hostname, no scheme) — CloudFront's /api/* origin."
  type        = string
}
