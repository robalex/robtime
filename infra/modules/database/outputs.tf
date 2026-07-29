output "endpoint" {
  description = "host:port — combine with db_name/username and the Secrets-Manager-resolved password to build the Npgsql connection string."
  value       = aws_db_instance.this.endpoint
}

output "db_name" {
  value = aws_db_instance.this.db_name
}

output "username" {
  value = aws_db_instance.this.username
}

output "master_user_secret_arn" {
  description = "Secrets Manager ARN holding the RDS-generated master password — the api module wires this into App Runner's RuntimeEnvironmentSecrets, never a plain env var."
  value       = aws_db_instance.this.master_user_secret[0].secret_arn
}
