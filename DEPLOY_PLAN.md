# RobTime — AWS Deployment Plan

Companion to `PLAN.md` (engine) and `UI_PLAN.md` (frontend/API). Nothing deployment-related exists
yet — no Dockerfile, no Terraform, no AWS resources. This is a from-scratch design, scoped for
**pre-launch, deploying soon, one staging environment, no custom domain yet** (per 2026-07-22
decisions — revisit the scope section below when either of those change).

---

## 1. Scope

**In scope now:** one `staging` environment, reachable over HTTPS on AWS-provided domains
(`*.awsapprunner.com`, `*.cloudfront.net`) — enough to deploy real builds and demo the app.

**Explicitly deferred, not forgotten:**
- **Production environment.** Stood up when a real client is imminent, not speculatively — at that
  point it gets the extra rigor staging doesn't need: Multi-AZ RDS, alerting, the separately-keyed
  backup story from `UI_PLAN.md` §5 (see §6 below), and probably a stricter change-approval gate on
  `terraform apply`.
- **Custom domain + Route53 + ACM.** Additive later: register/point a domain, request an ACM cert
  in `us-east-1` (CloudFront's hard requirement, regardless of what region everything else runs in),
  attach it as an alternate domain name on the CloudFront distribution. Nothing about the
  architecture below needs to change to add this.

---

## 2. Architecture

| Piece | Choice | Why |
|---|---|---|
| API compute | **AWS App Runner** | Container in, HTTPS/load-balancing/scaling out. A VPC Connector reaches RDS privately. Far less Terraform than ECS Fargate + ALB; ECS is the natural graduation path if/when specific networking control is needed — swapping the compute module later doesn't touch the network/database/frontend modules. |
| Database | **RDS PostgreSQL**, single-AZ, `db.t4g.small` (burstable, cheap) | Nothing in the engine needs Aurora-specific features (see `TimeCalculation.Persistence/README.md` — plain Npgsql + NodaTime). Aurora's advantages (read replicas, fast failover) matter at real scale, not for a pre-launch staging box. Multi-AZ is a production-only upgrade (§1). |
| Frontend | **S3 + CloudFront**, path-routed | `/api/*` → App Runner origin, everything else → S3 (the built Vite SPA, `UI_PLAN.md` §2), one CloudFront domain. This is what actually satisfies the same-origin cookie-auth decision (`UI_PLAN.md` §5, §9 item 9) — the browser only ever talks to one domain, whether that domain is AWS-provided or custom. |
| Secrets | **Secrets Manager** | RDS can auto-generate/rotate credentials into it; App Runner reads the connection string via IAM at deploy time — it never lives in Terraform state or a config file. |
| Registry | **ECR** | Needs a Dockerfile for `TimeCalculation.Api` — doesn't exist yet; that's `UI_PLAN.md` Phase 0 work, a prerequisite here (§5). |
| Region | **`us-east-1`**, default | Keeps everything in one region including the CloudFront-mandated `us-east-1` ACM cert once a domain exists. Override if you have a data-residency reason to prefer another region — nothing below assumes `us-east-1` specifically except the future ACM cert. |
| CI/CD | Extend the existing `.github/workflows/ci.yml` | Build → push image to ECR → deploy, via **GitHub OIDC federation to an IAM role** — no long-lived AWS access keys stored as repo secrets. This is the one piece that needs a manual, local, one-time bootstrap (§4) before CI can use it. |
| State | S3 backend + DynamoDB lock table, **directory per environment** (`environments/staging/`, `environments/production/`), not Terraform workspaces | Workspaces make it too easy to `apply` against the wrong environment by forgetting to switch. A directory forces you to look at where you are before running anything. |

---

## 3. Project layout

```
infra/
  bootstrap/                # applied once, locally, with your own AWS creds — see §4
    main.tf                 # S3 state bucket + DynamoDB lock table + GitHub OIDC provider/role
  environments/
    staging/
      main.tf                # backend "s3" {...} pointing at the bootstrap bucket; provider; module calls
      terraform.tfvars
    production/                # created when §1's trigger is hit — not before
  modules/
    network/                  # VPC, private subnets for RDS, security groups
    database/                  # RDS instance, subnet group, parameter group, Secrets Manager wiring
    api/                       # App Runner service, VPC connector, ECR repo, IAM roles
    frontend/                  # S3 bucket (private, OAC), CloudFront distribution + path routing
    dns/                       # Route53 + ACM — created but unused until §1's domain work starts
```

---

## 4. The bootstrapping problem — and why it needs you, not me

Two chicken-and-egg steps have to happen exactly once, by hand, before any of the above can be
automated:

1. **Remote state needs somewhere to live before Terraform can use a remote backend.** The
   `infra/bootstrap/` module is applied once with **local** state (no backend block) to create the
   S3 bucket + DynamoDB table; every environment after that points its backend at what bootstrap
   created.
2. **GitHub Actions needs an IAM role to assume before it can run Terraform itself** — and creating
   that OIDC provider + role is exactly the kind of AWS-account change that needs real, already-
   privileged credentials, which by definition can't come from the not-yet-existing CI pipeline.

**I have no AWS credentials in this environment** — `aws` isn't even installed on this machine yet.
I can write every `.tf` file below, but running `terraform apply` for the bootstrap step needs
credentials I can reach. The safe way to hand that off:

- You run `aws configure` (or `aws sso login`, if your org uses IAM Identity Center) **yourself**,
  in your own terminal — I will not ask you to paste an access key or secret into chat.
- Once `aws sts get-caller-identity` succeeds locally, tell me, and I can run `terraform plan`/
  `apply` for the bootstrap module via the same shell, since it'll pick up your configured
  credentials without either of us handling the key material directly.
- After bootstrap, ordinary `staging` applies can run from GitHub Actions via the OIDC role — no
  more local credential handling needed for day-to-day deploys.

I will always show you `terraform plan` output and wait for an explicit go-ahead before `apply` —
these create billable, not-trivially-reversible resources (RDS, CloudFront, DNS).

---

## 5. Dependency on `UI_PLAN.md`

This can't reach a deployed staging environment until two things exist on the app side:

- **A Dockerfile for `TimeCalculation.Api`** — doesn't exist yet. Straightforward (multi-stage
  `dotnet publish` build), but it's real work, not a copy-paste.
- **A working API** to put in the container — i.e., meaningful progress on `UI_PLAN.md` Phase 0.

Everything else here — Terraform module scaffolding, the bootstrap module, the network/database
modules — has no code dependency and can start in parallel. Suggest calling this **Phase 0b** in
`UI_PLAN.md`'s phase list: runs alongside Phase 0, first real deploy happens once Phase 0 has
something to containerize.

---

## 6. Open item carried over from `UI_PLAN.md` §5

RDS's default automated backups share the primary storage's KMS key — they don't satisfy the
"separately-keyed backups" line from the data-protection groundwork on their own. For staging (no
real PII yet) this doesn't matter. **Before production stands up**, this needs either a scheduled
`pg_dump` to S3 under a different key, or scheduled snapshot copies to a different key — noted here
so it isn't rediscovered under pressure later.

---

## 7. Rough cost expectation (staging)

Ballpark, not a quote — App Runner's smallest tier + `db.t4g.small` single-AZ + minimal S3/
CloudFront traffic should land in the **low tens of dollars/month** range while idle-ish during
development. Flagging so there's no surprise; actual AWS pricing depends on usage and changes over
time, so treat this as a sanity check, not a budget.

---

## 8. Next step

Nothing above is written as actual `.tf` yet — this is the architecture and sequencing for review.
Once confirmed, the concrete next actions are: install the Terraform CLI locally, scaffold
`infra/bootstrap/` and `infra/modules/`, and — once you've run `aws configure` — walk through the
bootstrap apply together.
