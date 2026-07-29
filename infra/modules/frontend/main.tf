# S3 + CloudFront, path-routed (DEPLOY_PLAN.md §2/§3). CloudFront is the one origin the browser ever
# talks to: /api/* forwards to App Runner, everything else serves the built Vite SPA from a private
# S3 bucket via Origin Access Control. This is what makes UI_PLAN.md's same-origin cookie-auth design
# work on AWS-provided domains, no custom domain required yet (dns module is unused until then).

data "aws_caller_identity" "current" {}

locals {
  name = "robtime-${var.environment}-frontend"
}

resource "aws_s3_bucket" "this" {
  bucket = "${local.name}-${data.aws_caller_identity.current.account_id}"

  tags = { Name = local.name }
}

resource "aws_s3_bucket_public_access_block" "this" {
  bucket = aws_s3_bucket.this.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "this" {
  bucket = aws_s3_bucket.this.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_cloudfront_origin_access_control" "this" {
  name                              = local.name
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

# Only CloudFront (via this specific distribution's OAC) may read the bucket — no public access,
# no OAI. aws_s3_bucket_public_access_block above blocks public ACLs/policies; this policy is the
# actual grant, scoped with the AWS:SourceArn condition to this distribution specifically.
data "aws_iam_policy_document" "bucket" {
  statement {
    sid       = "AllowCloudFrontOAC"
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.this.arn}/*"]

    principals {
      type        = "Service"
      identifiers = ["cloudfront.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "AWS:SourceArn"
      values   = [aws_cloudfront_distribution.this.arn]
    }
  }
}

resource "aws_s3_bucket_policy" "this" {
  bucket = aws_s3_bucket.this.id
  policy = data.aws_iam_policy_document.bucket.json
}

# AWS managed cache/origin-request policies (stable, documented IDs — no reason to hand-roll these).
locals {
  cache_policy_optimized            = "658327ea-f89d-4fab-a63d-7e88639e58f6" # CachingOptimized
  cache_policy_disabled             = "4135ea2d-6df8-44a3-9df3-4b5a84be39ad" # CachingDisabled
  origin_request_all_viewer_no_host = "b689b0a8-53d0-40ab-baf2-68738e2966ac" # AllViewerExceptHostHeader
}

resource "aws_cloudfront_distribution" "this" {
  enabled             = true
  default_root_object = "index.html"
  comment             = local.name

  origin {
    domain_name              = aws_s3_bucket.this.bucket_regional_domain_name
    origin_id                = "s3-frontend"
    origin_access_control_id = aws_cloudfront_origin_access_control.this.id
  }

  origin {
    domain_name = var.api_domain_name
    origin_id   = "apprunner-api"

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "https-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }
  }

  default_cache_behavior {
    allowed_methods        = ["GET", "HEAD"]
    cached_methods         = ["GET", "HEAD"]
    target_origin_id       = "s3-frontend"
    viewer_protocol_policy = "redirect-to-https"
    cache_policy_id        = local.cache_policy_optimized
  }

  # Cookies/query strings/every method forwarded, nothing cached — this is the API, not static
  # assets. AllViewerExceptHostHeader (not AllViewer) so CloudFront sets Host to the App Runner
  # origin's own domain rather than forwarding the CloudFront domain, which App Runner's default
  # *.awsapprunner.com routing depends on.
  ordered_cache_behavior {
    path_pattern             = "/api/*"
    allowed_methods          = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods           = ["GET", "HEAD"]
    target_origin_id         = "apprunner-api"
    viewer_protocol_policy   = "https-only"
    cache_policy_id          = local.cache_policy_disabled
    origin_request_policy_id = local.origin_request_all_viewer_no_host
  }

  # No custom domain yet (DEPLOY_PLAN.md §1) — the default *.cloudfront.net cert.
  viewer_certificate {
    cloudfront_default_certificate = true
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  # React Router client-side routes (e.g. /clients/123) aren't real S3 keys — the OAC origin
  # returns 403 (not 404) for a missing key, so both need to fall back to index.html and let the
  # SPA's router take over.
  custom_error_response {
    error_code         = 403
    response_code      = 200
    response_page_path = "/index.html"
  }

  custom_error_response {
    error_code         = 404
    response_code      = 200
    response_page_path = "/index.html"
  }

  tags = { Name = local.name }
}
