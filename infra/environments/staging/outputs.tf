output "api_service_url" {
  value = module.api.service_url
}

output "frontend_url" {
  description = "The app's actual address — https:// + this."
  value       = module.frontend.domain_name
}

output "ecr_repository_url" {
  description = "For the CI workflow's docker build/push step."
  value       = module.api.ecr_repository_url
}

output "cognito_user_pool_id" {
  value = module.identity.user_pool_id
}

output "cognito_user_pool_client_id" {
  value = module.identity.user_pool_client_id
}

output "cognito_hosted_ui_domain" {
  value = module.identity.hosted_ui_domain
}
