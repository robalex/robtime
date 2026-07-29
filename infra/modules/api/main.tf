# ECR + App Runner + VPC Connector + IAM (DEPLOY_PLAN.md §2/§3). Builds and runs the image published
# by TimeCalculation.Api/Dockerfile; the connection string and Cognito admin permissions are wired in
# at runtime, never through Terraform (see the instance-role secret policy below).

locals {
  name    = "robtime-${var.environment}-api"
  db_host = split(":", var.db_endpoint)[0]
  db_port = split(":", var.db_endpoint)[1]
}

resource "aws_ecr_repository" "this" {
  name                 = local.name
  image_tag_mutability = "MUTABLE" # CI re-pushes the same "latest" tag on every deploy — see auto_deployments_enabled below.

  image_scanning_configuration {
    scan_on_push = true
  }

  tags = { Name = local.name }
}

# Untagged images pile up on every rebuild (each push to "latest" orphans the previous digest);
# nothing else references them, so there's no reason to keep more than a handful around.
resource "aws_ecr_lifecycle_policy" "this" {
  repository = aws_ecr_repository.this.name

  policy = jsonencode({
    rules = [{
      rulePriority = 1
      description  = "Expire untagged images after 10 are kept"
      selection = {
        tagStatus   = "untagged"
        countType   = "imageCountMoreThan"
        countNumber = 10
      }
      action = { type = "expire" }
    }]
  })
}

resource "aws_apprunner_vpc_connector" "this" {
  vpc_connector_name = local.name
  subnets            = var.private_subnet_ids
  security_groups    = [var.vpc_connector_security_group_id]
}

# The "access role" — App Runner assumes this to pull the image from ECR at deploy time. Distinct
# from the instance role below (which the running container itself assumes); AWS models these as two
# separate trust relationships (build.apprunner.amazonaws.com vs tasks.apprunner.amazonaws.com).
data "aws_iam_policy_document" "access_trust" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["build.apprunner.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "access" {
  name               = "${local.name}-access"
  assume_role_policy = data.aws_iam_policy_document.access_trust.json
}

resource "aws_iam_role_policy_attachment" "access_ecr" {
  role       = aws_iam_role.access.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSAppRunnerServicePolicyForECRAccess"
}

# The "instance role" — the running container's runtime identity. Used for two things: resolving
# RuntimeEnvironmentSecrets (Database__Password, below) at deploy time, and the Cognito AdminCreate/
# Delete/Get/UpdateUserAttributes calls TimeCalculation.Api/Auth/CognitoUserProvisioner.cs and
# AdminBootstrapper.cs make at request time via the default AWS credential chain.
data "aws_iam_policy_document" "instance_trust" {
  statement {
    actions = ["sts:AssumeRole"]
    principals {
      type        = "Service"
      identifiers = ["tasks.apprunner.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "instance" {
  name               = "${local.name}-instance"
  assume_role_policy = data.aws_iam_policy_document.instance_trust.json
}

data "aws_iam_policy_document" "instance_permissions" {
  statement {
    sid       = "ReadDbSecret"
    actions   = ["secretsmanager:GetSecretValue"]
    resources = [var.db_master_user_secret_arn]
  }

  statement {
    sid = "CognitoAdminUserManagement"
    actions = [
      "cognito-idp:AdminCreateUser",
      "cognito-idp:AdminDeleteUser",
      "cognito-idp:AdminGetUser",
      "cognito-idp:AdminUpdateUserAttributes",
    ]
    resources = [var.cognito_user_pool_arn]
  }
}

resource "aws_iam_role_policy" "instance_permissions" {
  name   = "${local.name}-instance"
  role   = aws_iam_role.instance.id
  policy = data.aws_iam_policy_document.instance_permissions.json
}

resource "aws_apprunner_service" "this" {
  service_name = local.name

  source_configuration {
    auto_deployments_enabled = true

    authentication_configuration {
      access_role_arn = aws_iam_role.access.arn
    }

    image_repository {
      image_identifier      = "${aws_ecr_repository.this.repository_url}:${var.image_tag}"
      image_repository_type = "ECR"

      image_configuration {
        port = "8080" # Matches ASPNETCORE_URLS/EXPOSE in TimeCalculation.Api/Dockerfile.

        runtime_environment_variables = {
          ASPNETCORE_ENVIRONMENT    = title(var.environment)
          Cognito__Region           = var.aws_region
          Cognito__UserPoolId       = var.cognito_user_pool_id
          Cognito__UserPoolClientId = var.cognito_user_pool_client_id
          Database__Host            = local.db_host
          Database__Port            = local.db_port
          Database__Name            = var.db_name
          Database__Username        = var.db_username
        }

        # Pulled directly from the RDS-managed secret's JSON at deploy time via the instance role —
        # Program.cs composes the final Npgsql connection string from this plus the plain Database__*
        # vars above (ConnectionStrings__PayrollDb never appears in Terraform state or here).
        runtime_environment_secrets = {
          Database__Password = "${var.db_master_user_secret_arn}:password::"
        }
      }
    }
  }

  instance_configuration {
    cpu               = var.cpu
    memory            = var.memory
    instance_role_arn = aws_iam_role.instance.arn
  }

  network_configuration {
    egress_configuration {
      egress_type       = "VPC"
      vpc_connector_arn = aws_apprunner_vpc_connector.this.arn
    }
  }

  # TCP, not HTTP+path — there's no dedicated health endpoint (no /healthz route in Program.cs) and
  # adding one purely for this would be scope creep beyond the infra work; a TCP check against the
  # Kestrel listener port is a reasonable liveness signal on its own.
  health_check_configuration {
    protocol = "TCP"
  }

  tags = { Name = local.name }
}
