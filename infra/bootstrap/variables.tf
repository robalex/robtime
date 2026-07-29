variable "aws_region" {
  description = "AWS region for the state bucket, lock table, and IAM resources. DEPLOY_PLAN.md §2 defaults everything to us-east-1."
  type        = string
  default     = "us-east-1"
}

variable "github_repository" {
  description = "GitHub \"org/repo\" the OIDC trust policy scopes access to. Must match exactly what GitHub Actions presents as the `repo:` claim."
  type        = string
  default     = "robalex/robtime"
}
