output "user_pool_id" {
  description = "Maps to the app's Cognito:UserPoolId config key."
  value       = aws_cognito_user_pool.this.id
}

output "user_pool_client_id" {
  description = "Maps to the app's Cognito:UserPoolClientId config key (the JWT `aud` claim)."
  value       = aws_cognito_user_pool_client.spa.id
}

output "user_pool_arn" {
  description = "For scoping the App Runner instance role's cognito-idp:Admin* permissions to this pool specifically."
  value       = aws_cognito_user_pool.this.arn
}

output "hosted_ui_domain" {
  description = "Full Hosted UI domain the SPA's authorizeUrl()/tokenUrl()/logoutUrl() (auth/config.ts) build URLs against."
  value       = "${aws_cognito_user_pool_domain.this.domain}.auth.${data.aws_region.current.name}.amazoncognito.com"
}

data "aws_region" "current" {}
