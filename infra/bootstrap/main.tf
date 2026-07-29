# Applied ONCE, locally, with your own AWS credentials — see DEPLOY_PLAN.md §4 for why this can't
# be automated: it creates the remote state backend that every other Terraform config (including
# this one, on any run after the first) would otherwise need in order to run at all, and it creates
# the IAM role GitHub Actions needs before GitHub Actions can be trusted to run Terraform itself.
#
# Deliberately has NO `backend` block — this is the one config in the whole repo whose own state
# lives locally (or wherever you run it from). Point of order: don't add a backend block here later
# without moving this state into it first (`terraform state mv`/`init -migrate-state`), or you'll
# orphan the very thing that manages the state bucket.

terraform {
  required_version = ">= 1.9"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

data "aws_caller_identity" "current" {}

# ---------------------------------------------------------------------------
# Remote state backend: S3 bucket (state files) + DynamoDB table (locks).
# DEPLOY_PLAN.md §2 chose S3+DynamoDB explicitly over Terraform workspaces —
# a directory-per-environment forces you to look at where you are before
# applying, which workspaces make too easy to skip.
# ---------------------------------------------------------------------------

resource "aws_s3_bucket" "terraform_state" {
  # Account id suffix guarantees global uniqueness without a random_id resource whose value would
  # otherwise need to be recorded somewhere outside Terraform state to survive a re-run.
  bucket = "robtime-terraform-state-${data.aws_caller_identity.current.account_id}"

  # This bucket is infrastructure-of-infrastructure — losing it loses every environment's state.
  # force_destroy stays false; if it ever genuinely needs deleting, empty it by hand first as a
  # deliberate, separate action.
  lifecycle {
    prevent_destroy = true
  }
}

resource "aws_s3_bucket_versioning" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id
  versioning_configuration {
    # State corruption or an accidental `apply` from a stale plan should be recoverable from a prior
    # version, not just from whatever the last write happened to be.
    status = "Enabled"
  }
}

resource "aws_s3_bucket_server_side_encryption_configuration" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_public_access_block" "terraform_state" {
  bucket = aws_s3_bucket.terraform_state.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_dynamodb_table" "terraform_locks" {
  name         = "robtime-terraform-locks"
  billing_mode = "PAY_PER_REQUEST" # Lock traffic is one row per apply — provisioned capacity buys nothing here.
  hash_key     = "LockID"

  attribute {
    name = "LockID"
    type = "S"
  }
}

# ---------------------------------------------------------------------------
# GitHub OIDC federation — lets GitHub Actions assume an AWS role with a
# short-lived token instead of a long-lived access key stored as a repo
# secret (DEPLOY_PLAN.md §2's CI/CD row).
# ---------------------------------------------------------------------------

# Fetched at apply time rather than hardcoded: this thumbprint is nominally the root CA in GitHub's
# TLS chain, and those have rotated before (Let's Encrypt's cross-sign expiry broke every hardcoded
# copy of this value industry-wide). AWS has validated OIDC federation against its own trusted root
# store for well-known providers like this one since 2023 rather than this exact value, but the
# resource still requires a syntactically valid thumbprint — computing it avoids re-encountering a
# stale hardcoded one when it doesn't matter functionally.
data "tls_certificate" "github_actions" {
  url = "https://token.actions.githubusercontent.com"
}

resource "aws_iam_openid_connect_provider" "github_actions" {
  url             = "https://token.actions.githubusercontent.com"
  client_id_list  = ["sts.amazonaws.com"]
  thumbprint_list = [data.tls_certificate.github_actions.certificates[length(data.tls_certificate.github_actions.certificates) - 1].sha1_fingerprint]
}

data "aws_iam_policy_document" "github_actions_trust" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRoleWithWebIdentity"]

    principals {
      type        = "Federated"
      identifiers = [aws_iam_openid_connect_provider.github_actions.arn]
    }

    condition {
      test     = "StringEquals"
      variable = "token.actions.githubusercontent.com:aud"
      values   = ["sts.amazonaws.com"]
    }

    # Scoped to this one repo, any ref — covers both `pull_request` runs (plan) and pushes to main
    # (apply). Tightening apply-capable actions to `ref:refs/heads/main` specifically is a
    # `workflow`-level decision (which job requests which role), not something the trust policy
    # needs to enforce redundantly, but revisit this if a stricter split is ever wanted.
    condition {
      test     = "StringLike"
      variable = "token.actions.githubusercontent.com:sub"
      values   = ["repo:${var.github_repository}:*"]
    }
  }
}

resource "aws_iam_role" "github_actions" {
  name               = "robtime-github-actions"
  assume_role_policy = data.aws_iam_policy_document.github_actions_trust.json
}

# Scoped to the services DEPLOY_PLAN.md §2's architecture table actually names, not admin-on-account.
# Intentionally broader within each service than a hand-tuned least-privilege policy would be —
# staging doesn't warrant that rigor yet (§1); tighten before production gets its own role.
data "aws_iam_policy_document" "github_actions_permissions" {
  statement {
    sid    = "TerraformState"
    effect = "Allow"
    actions = [
      "s3:GetObject",
      "s3:PutObject",
      "s3:ListBucket",
    ]
    resources = [
      aws_s3_bucket.terraform_state.arn,
      "${aws_s3_bucket.terraform_state.arn}/*",
    ]
  }

  statement {
    sid    = "TerraformLocks"
    effect = "Allow"
    actions = [
      "dynamodb:GetItem",
      "dynamodb:PutItem",
      "dynamodb:DeleteItem",
    ]
    resources = [aws_dynamodb_table.terraform_locks.arn]
  }

  statement {
    sid    = "Network"
    effect = "Allow"
    actions = [
      "ec2:*Vpc*",
      "ec2:*Subnet*",
      "ec2:*SecurityGroup*",
      "ec2:*RouteTable*",
      "ec2:*InternetGateway*",
      "ec2:*NatGateway*",
      "ec2:*Address*",
      "ec2:DescribeAvailabilityZones",
      "ec2:CreateTags",
      "ec2:DeleteTags",
      "ec2:DescribeTags",
    ]
    resources = ["*"]
  }

  statement {
    sid    = "Database"
    effect = "Allow"
    actions = [
      "rds:*",
      "secretsmanager:*",
    ]
    resources = ["*"]
  }

  statement {
    sid    = "Api"
    effect = "Allow"
    actions = [
      "apprunner:*",
      "ecr:*",
    ]
    resources = ["*"]
  }

  statement {
    sid    = "Frontend"
    effect = "Allow"
    actions = [
      "s3:*",
      "cloudfront:*",
    ]
    resources = ["*"]
  }

  statement {
    sid    = "Dns"
    effect = "Allow"
    actions = [
      "route53:*",
      "acm:*",
    ]
    resources = ["*"]
  }

  statement {
    sid    = "Identity"
    effect = "Allow"
    actions = [
      "cognito-idp:*",
    ]
    resources = ["*"]
  }

  # App Runner's instance role, the RDS-to-Secrets-Manager wiring, and the VPC connector all need
  # Terraform to create/attach IAM roles and policies on the app's behalf. Scoped to role names this
  # repo's modules create (the "robtime-" prefix) rather than iam:* — CI should never be able to
  # touch IAM outside what its own modules manage.
  statement {
    sid    = "AppIamRoles"
    effect = "Allow"
    actions = [
      "iam:CreateRole",
      "iam:DeleteRole",
      "iam:GetRole",
      "iam:PassRole",
      "iam:TagRole",
      "iam:UntagRole",
      "iam:PutRolePolicy",
      "iam:DeleteRolePolicy",
      "iam:GetRolePolicy",
      "iam:AttachRolePolicy",
      "iam:DetachRolePolicy",
      "iam:ListRolePolicies",
      "iam:ListAttachedRolePolicies",
    ]
    resources = ["arn:aws:iam::${data.aws_caller_identity.current.account_id}:role/robtime-*"]
  }
}

resource "aws_iam_role_policy" "github_actions" {
  name   = "robtime-github-actions-permissions"
  role   = aws_iam_role.github_actions.id
  policy = data.aws_iam_policy_document.github_actions_permissions.json
}
