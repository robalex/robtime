output "state_bucket_name" {
  description = "S3 bucket every environment's backend block should point at."
  value       = aws_s3_bucket.terraform_state.id
}

output "lock_table_name" {
  description = "DynamoDB table every environment's backend block should point at."
  value       = aws_dynamodb_table.terraform_locks.name
}

output "github_actions_role_arn" {
  description = "Role ARN for the CI workflow to assume via aws-actions/configure-aws-credentials' role-to-assume input."
  value       = aws_iam_role.github_actions.arn
}
