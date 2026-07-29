# Staging environment root (DEPLOY_PLAN.md §2/§3). Wires network -> database/identity -> api ->
# frontend together. The dns module is deliberately not called here — no custom domain yet (§1).
#
# Prerequisite: infra/bootstrap must already be applied (once, by hand — see DEPLOY_PLAN.md §4) —
# it was, 2026-07-27; bucket/table below are its `state_bucket_name`/`lock_table_name` outputs.

terraform {
  required_version = ">= 1.9"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }

  backend "s3" {
    bucket         = "robtime-terraform-state-234585270334"
    key            = "staging/terraform.tfstate"
    region         = "us-east-1"
    dynamodb_table = "robtime-terraform-locks"
    encrypt        = true
  }
}

provider "aws" {
  region = var.aws_region
}

module "network" {
  source = "../../modules/network"

  environment = var.environment
}

module "database" {
  source = "../../modules/database"

  environment        = var.environment
  private_subnet_ids = module.network.private_subnet_ids
  security_group_id  = module.network.database_security_group_id
}

module "identity" {
  source = "../../modules/identity"

  environment             = var.environment
  hosted_ui_domain_prefix = var.hosted_ui_domain_prefix
  callback_urls           = var.callback_urls
  logout_urls             = var.logout_urls
  app_url                 = var.app_url
}

module "api" {
  source = "../../modules/api"

  environment                     = var.environment
  aws_region                      = var.aws_region
  private_subnet_ids              = module.network.private_subnet_ids
  vpc_connector_security_group_id = module.network.vpc_connector_security_group_id

  db_endpoint               = module.database.endpoint
  db_name                   = module.database.db_name
  db_username               = module.database.username
  db_master_user_secret_arn = module.database.master_user_secret_arn

  cognito_user_pool_id        = module.identity.user_pool_id
  cognito_user_pool_client_id = module.identity.user_pool_client_id
  cognito_user_pool_arn       = module.identity.user_pool_arn
}

module "frontend" {
  source = "../../modules/frontend"

  environment     = var.environment
  api_domain_name = module.api.service_url
}
