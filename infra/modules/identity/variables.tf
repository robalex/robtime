variable "environment" {
  description = "Environment name (e.g. \"staging\"), used in resource names."
  type        = string
}

variable "hosted_ui_domain_prefix" {
  description = "Cognito Hosted UI domain prefix — must be globally unique across every Cognito pool in the region. Resulting domain is \"<prefix>.auth.<region>.amazoncognito.com\"."
  type        = string
}

variable "callback_urls" {
  description = "URLs Cognito may redirect back to after sign-in — RobTimeUI's `$${origin}/auth/callback` (auth/config.ts) for each origin the SPA is actually served from."
  type        = list(string)
}

variable "logout_urls" {
  description = "URLs Cognito may redirect back to after sign-out — RobTimeUI's `$${origin}/` for each origin the SPA is actually served from."
  type        = list(string)
}

variable "app_url" {
  description = "Where the invite email tells a new user to sign in. Same chicken-and-egg as callback_urls/logout_urls — the frontend module's real domain isn't known until its first apply, so this defaults to a placeholder string until you fill it in and re-apply."
  type        = string
  default     = "your organization's RobTime URL"
  nullable    = false # So an explicit null from the caller (staging's own unset default) falls back to the default above instead of erroring in the invite_message_template string.
}
