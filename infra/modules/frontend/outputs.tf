output "bucket_name" {
  description = "For the CI workflow's `aws s3 sync` step deploying the built Vite SPA."
  value       = aws_s3_bucket.this.id
}

output "distribution_id" {
  description = "For the CI workflow's cache invalidation step after each deploy."
  value       = aws_cloudfront_distribution.this.id
}

output "domain_name" {
  description = "The CloudFront domain the app is actually reached at (*.cloudfront.net until DEPLOY_PLAN.md §1's custom-domain work)."
  value       = aws_cloudfront_distribution.this.domain_name
}
