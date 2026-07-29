output "zone_id" {
  value = aws_route53_zone.this.zone_id
}

output "name_servers" {
  description = "Point the domain registrar's NS records here to delegate DNS to this zone."
  value       = aws_route53_zone.this.name_servers
}

output "certificate_arn" {
  description = "For the frontend module's CloudFront distribution viewer_certificate.acm_certificate_arn once this is wired in."
  value       = aws_acm_certificate_validation.this.certificate_arn
}
