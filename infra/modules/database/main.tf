# RDS PostgreSQL (DEPLOY_PLAN.md §2/§3). Single-AZ, db.t4g.small — nothing in the engine needs
# Aurora (TimeCalculation.Persistence/README.md: plain Npgsql + NodaTime), and Multi-AZ is an
# explicit production-only upgrade (§1). Version 16 matches TimeCalculation.Api.Tests/ApiFixture.cs's
# Testcontainers image ("postgres:16-alpine") — staging should run the same major version the test
# suite already validates against.

resource "aws_db_subnet_group" "this" {
  name       = "robtime-${var.environment}"
  subnet_ids = var.private_subnet_ids

  tags = { Name = "robtime-${var.environment}" }
}

resource "aws_db_instance" "this" {
  identifier     = "robtime-${var.environment}"
  engine         = "postgres"
  engine_version = "16"
  instance_class = var.instance_class

  allocated_storage = 20
  storage_type      = "gp3"
  storage_encrypted = true

  db_name  = "robtime"
  username = "robtime_app"

  # RDS generates and rotates the master password into Secrets Manager itself — the connection
  # string never lives in Terraform state or a config file (DEPLOY_PLAN.md §2's Secrets row). The
  # api module reads the resulting secret ARN to wire it into App Runner's runtime environment.
  manage_master_user_password = true

  db_subnet_group_name   = aws_db_subnet_group.this.name
  vpc_security_group_ids = [var.security_group_id]

  multi_az = false # Production-only upgrade — see this module's own doc comment.

  # Staging accepts the default backups-share-the-primary's-KMS-key limitation noted in
  # DEPLOY_PLAN.md §6 — there's no real PII in staging yet. Revisit before production.
  backup_retention_period = 7
  skip_final_snapshot     = true # Staging: losing the instance shouldn't require a snapshot step to redeploy.

  deletion_protection = false # Staging only — flip true (or let production's stricter module default differ) once this holds real data.

  auto_minor_version_upgrade = true
  apply_immediately          = var.apply_immediately

  tags = { Name = "robtime-${var.environment}" }
}
