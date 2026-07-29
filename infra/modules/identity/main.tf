# Cognito User Pool + App Client (DEPLOY_PLAN.md §3, UI_PLAN.md §5/Phase 1). The application code
# (JwtBearer validation in Program.cs, CognitoUserProvisioner, AdminBootstrapper) is already written
# against this module's eventual outputs — it's been running against a real pool created by hand in
# the console for local dev (RobTimeUI/README.md's "Local configuration" step 4); this module is
# what makes a pool exist for staging without that manual step.
#
# Schema names below (`custom:client_id`, `custom:role`) MUST match TimeCalculation.Api/Auth/
# TenantClaimTypes.cs exactly — the API reads these as literal claim names, not by any Terraform-side
# mapping.

resource "aws_cognito_user_pool" "this" {
  name = "robtime-${var.environment}"

  # Email as the sign-in identifier (UI_PLAN.md §5's "Login: email + password" decision) — no
  # separate username.
  username_attributes      = ["email"]
  auto_verified_attributes = ["email"]

  # AdminCreateUser (CognitoUserProvisioner) is how every user account is actually created — see
  # ICognitoUserProvisioner's doc comment — so self-registration stays off.
  #
  # CognitoUserProvisioner.CreateUserAsync already passes DesiredDeliveryMediums = ["EMAIL"] and
  # never suppresses the message, so Cognito sends this invite automatically on every AdminCreateUser
  # call — no SES setup or custom token/link page needed (UI_PLAN.md §10 Q2: invite flow, not
  # admin-set password). {####} is Cognito's required placeholder for the generated temporary
  # password; {username} is the email address (username_attributes = ["email"] above). Login itself
  # is Cognito's Hosted UI (RobTimeUI/src/auth/config.ts's authorizeUrl — a full-page redirect, not a
  # form this app renders), which detects the account's FORCE_CHANGE_PASSWORD status on its own and
  # prompts for a new password before completing the OAuth redirect back to the SPA — nothing else to
  # build for that step.
  admin_create_user_config {
    allow_admin_create_user_only = true

    invite_message_template {
      email_subject = "You've been invited to RobTime"
      email_message = "Hi,\n\nYou've been added to RobTime. Sign in at ${var.app_url} with:\n\nUsername: {username}\nTemporary password: {####}\n\nYou'll be asked to choose your own password the first time you sign in.\n\n— RobTime"
    }
  }

  password_policy {
    minimum_length    = 12
    require_lowercase = true
    require_uppercase = true
    require_numbers   = true
    require_symbols   = false
  }

  account_recovery_setting {
    recovery_mechanism {
      name     = "verified_email"
      priority = 1
    }
  }

  # String, not Number — TenantClaimTypes.ClientId/Role are read as plain string claims throughout
  # the API (see CallerIdentity, HttpContextTenantContextAccessor), never parsed via a Cognito-typed
  # attribute.
  schema {
    name                     = "client_id"
    attribute_data_type      = "String"
    mutable                  = true
    developer_only_attribute = false
    string_attribute_constraints {
      min_length = 1
      max_length = 16
    }
  }

  schema {
    name                     = "role"
    attribute_data_type      = "String"
    mutable                  = true
    developer_only_attribute = false
    string_attribute_constraints {
      min_length = 1
      max_length = 32
    }
  }

  lifecycle {
    # Custom attribute schema can't be changed in place once users exist against it — Cognito
    # rejects the update. If client_id/role ever need to change shape, that's a new pool + user
    # migration, not a `terraform apply`.
    prevent_destroy = true
  }
}

# The managed login (Hosted UI) domain the browser signs in against — see RobTimeUI/src/auth/
# config.ts's own doc comment distinguishing this from the OIDC issuer URL.
resource "aws_cognito_user_pool_domain" "this" {
  domain       = var.hosted_ui_domain_prefix
  user_pool_id = aws_cognito_user_pool.this.id
}

# Public SPA client — no secret (a browser can't keep one), PKCE-based authorization code flow.
# Matches RobTimeUI/src/auth/pkce.ts's flow exactly.
resource "aws_cognito_user_pool_client" "spa" {
  name         = "robtime-ui-${var.environment}"
  user_pool_id = aws_cognito_user_pool.this.id

  generate_secret = false

  allowed_oauth_flows_user_pool_client = true
  allowed_oauth_flows                  = ["code"]
  allowed_oauth_scopes                 = ["openid", "email", "profile"]
  supported_identity_providers         = ["COGNITO"]

  callback_urls = var.callback_urls
  logout_urls   = var.logout_urls

  explicit_auth_flows = ["ALLOW_REFRESH_TOKEN_AUTH", "ALLOW_USER_SRP_AUTH"]

  prevent_user_existence_errors = "ENABLED"

  access_token_validity  = 60 # minutes
  id_token_validity      = 60 # minutes
  refresh_token_validity = 30 # days
  token_validity_units {
    access_token  = "minutes"
    id_token      = "minutes"
    refresh_token = "days"
  }
}
