output "service_url" {
  description = "App Runner's default HTTPS endpoint (*.awsapprunner.com) — the frontend module points CloudFront's /api/* origin here."
  value       = aws_apprunner_service.this.service_url
}

output "service_arn" {
  value = aws_apprunner_service.this.arn
}

output "ecr_repository_url" {
  description = "Where CI pushes the built image (DEPLOY_PLAN.md's CI/CD row) — <this>:latest matches source_configuration.image_repository.image_identifier."
  value       = aws_ecr_repository.this.repository_url
}

output "ecr_repository_name" {
  description = "For the CI workflow's `aws ecr get-login-password` / push steps."
  value       = aws_ecr_repository.this.name
}
