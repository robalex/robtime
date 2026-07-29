# VPC, subnets, and security groups (DEPLOY_PLAN.md §3). Private subnets host the RDS instance and
# the App Runner VPC Connector's ENIs; public subnets exist only to give the NAT Gateway somewhere
# to live. App Runner's service itself is never "in" this VPC — attaching a VPC Connector routes
# *all* of its outbound traffic (not just the RDS-bound portion) through here, which is why a NAT
# Gateway is required at all: without one, the app loses internet access entirely once connected,
# including the outbound calls it needs to make to Cognito's public JWKS endpoint.
#
# Single NAT Gateway, not one per AZ — matches the single-AZ RDS choice for staging (DEPLOY_PLAN.md
# §2): cheaper, and an AZ outage already takes the database with it, so redundant NAT buys nothing
# staging needs. Revisit alongside Multi-AZ RDS when production stands up (§1).

data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  azs = slice(data.aws_availability_zones.available.names, 0, 2)
}

resource "aws_vpc" "this" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = { Name = "robtime-${var.environment}" }
}

resource "aws_internet_gateway" "this" {
  vpc_id = aws_vpc.this.id
  tags   = { Name = "robtime-${var.environment}" }
}

resource "aws_subnet" "public" {
  count                   = length(local.azs)
  vpc_id                  = aws_vpc.this.id
  cidr_block              = cidrsubnet(var.vpc_cidr, 8, count.index)
  availability_zone       = local.azs[count.index]
  map_public_ip_on_launch = true

  tags = { Name = "robtime-${var.environment}-public-${local.azs[count.index]}" }
}

resource "aws_subnet" "private" {
  count             = length(local.azs)
  vpc_id            = aws_vpc.this.id
  cidr_block        = cidrsubnet(var.vpc_cidr, 8, count.index + 10)
  availability_zone = local.azs[count.index]

  tags = { Name = "robtime-${var.environment}-private-${local.azs[count.index]}" }
}

resource "aws_eip" "nat" {
  domain = "vpc"
  tags   = { Name = "robtime-${var.environment}-nat" }
}

# In the first public subnet only — see the module doc comment on single-NAT for why this is one
# resource, not `count = length(local.azs)`.
resource "aws_nat_gateway" "this" {
  allocation_id = aws_eip.nat.id
  subnet_id     = aws_subnet.public[0].id
  tags          = { Name = "robtime-${var.environment}" }

  depends_on = [aws_internet_gateway.this]
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.this.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.this.id
  }

  tags = { Name = "robtime-${var.environment}-public" }
}

resource "aws_route_table_association" "public" {
  count          = length(aws_subnet.public)
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

resource "aws_route_table" "private" {
  vpc_id = aws_vpc.this.id

  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.this.id
  }

  tags = { Name = "robtime-${var.environment}-private" }
}

resource "aws_route_table_association" "private" {
  count          = length(aws_subnet.private)
  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = aws_route_table.private.id
}

# Attached to the App Runner VPC Connector (api module) — egress-only, since the connector only
# ever initiates connections outward (to RDS, and everything else once it owns all egress).
resource "aws_security_group" "vpc_connector" {
  name_prefix = "robtime-${var.environment}-connector-"
  description = "App Runner VPC Connector - outbound only"
  vpc_id      = aws_vpc.this.id

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  lifecycle {
    create_before_destroy = true
  }

  tags = { Name = "robtime-${var.environment}-connector" }
}

# Attached to the RDS instance (database module) — ingress from the VPC Connector's security group
# only, never a CIDR block, so it stays correct if subnets are ever resized.
resource "aws_security_group" "database" {
  name_prefix = "robtime-${var.environment}-database-"
  description = "RDS PostgreSQL - inbound from the App Runner VPC Connector only"
  vpc_id      = aws_vpc.this.id

  ingress {
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.vpc_connector.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  lifecycle {
    create_before_destroy = true
  }

  tags = { Name = "robtime-${var.environment}-database" }
}
