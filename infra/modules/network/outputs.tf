output "vpc_id" {
  value = aws_vpc.this.id
}

output "private_subnet_ids" {
  description = "For the RDS subnet group (database module) and the App Runner VPC Connector (api module)."
  value       = aws_subnet.private[*].id
}

output "public_subnet_ids" {
  value = aws_subnet.public[*].id
}

output "vpc_connector_security_group_id" {
  description = "Attach to the App Runner VPC Connector (api module)."
  value       = aws_security_group.vpc_connector.id
}

output "database_security_group_id" {
  description = "Attach to the RDS instance (database module)."
  value       = aws_security_group.database.id
}
