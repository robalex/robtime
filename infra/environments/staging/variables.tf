variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "environment" {
  type    = string
  default = "staging"
}

variable "hosted_ui_domain_prefix" {
  description = "Cognito Hosted UI domain prefix — must be globally unique across every Cognito pool in the region."
  type        = string
  default     = "robtime-staging"
}

variable "callback_urls" {
  description = "URLs Cognito may redirect back to after sign-in. Starts with just local dev; add the CloudFront domain's `/auth/callback` (module.frontend.domain_name output, only known after the first apply) and re-apply the identity module once it exists."
  type        = list(string)
  default     = ["http://localhost:5173/auth/callback"]
}

variable "logout_urls" {
  description = "URLs Cognito may redirect back to after sign-out. Same chicken-and-egg as callback_urls — see its comment."
  type        = list(string)
  default     = ["http://localhost:5173/"]
}

variable "app_url" {
  description = "Where the invite email tells new users to sign in. Same chicken-and-egg as callback_urls — fill in the real CloudFront domain and re-apply once frontend exists; the identity module's own default placeholder text covers it until then."
  type        = string
  default     = null
}
