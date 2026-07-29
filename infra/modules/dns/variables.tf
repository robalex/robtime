variable "domain_name" {
  description = "The registered domain to serve the app from (e.g. \"app.example.com\"). Not created/called by any environment yet (DEPLOY_PLAN.md §1: custom domain is explicitly deferred) — set this and add the module block to environments/staging/main.tf when that work starts."
  type        = string
}

variable "cloudfront_distribution_domain_name" {
  description = "The frontend module's `domain_name` output — the CloudFront alias target."
  type        = string
}

variable "cloudfront_distribution_hosted_zone_id" {
  description = "CloudFront's fixed hosted zone ID (Z2FDTNDATAQYW2) for alias records — pass through explicitly rather than hardcoding it twice."
  type        = string
  default     = "Z2FDTNDATAQYW2"
}
