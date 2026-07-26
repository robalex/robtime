# RobTimeUI

Frontend for RobTime — Vite + React 19 + TypeScript (strict), TanStack Router + Query, Tailwind v4 +
shadcn/ui. See `../UI_PLAN.md` for the phased build plan and the decisions behind this stack (§2).

## Develop

**Both servers are needed.** In two terminals:

```bash
npm run dev:api        # the .NET API on http://localhost:53534
npm run dev            # Vite dev server on http://localhost:5173
```

Open `http://localhost:5173`. The Vite server proxies `/api/*` to the API (stripping the `/api`
prefix), so the SPA and API are same-origin in dev — see `vite.config.ts`.

If the app shows **"Could not load your account"** after signing in, the API isn't running — that
message covers any failed `/me` call, including a plain connection refusal. `npm run dev:api` is the
fix.

## Local configuration (fill these in yourself)

Real Cognito identifiers are deliberately **not** in the repo. Both files below are gitignored, so
values you put in them stay on your machine.

**1. Frontend — `RobTimeUI/.env.local`**

```bash
cp .env.example .env.local     # then edit
```

| Variable | Where to find it in the AWS console |
|---|---|
| `VITE_COGNITO_DOMAIN` | Cognito → your pool → **Applications → Domain**. Host only: `your-prefix.auth.us-east-1.amazoncognito.com` (no `https://`, no trailing slash). |
| `VITE_COGNITO_CLIENT_ID` | **Applications → App clients → Client ID** |

**2. API — .NET user secrets** (stored in your user profile, not the repo):

```bash
cd TimeCalculation.Api
dotnet user-secrets set "Cognito:Region" "us-east-1"
dotnet user-secrets set "Cognito:UserPoolId" "us-east-1_XXXXXXXXX"
dotnet user-secrets set "Cognito:UserPoolClientId" "your-app-client-id"
```

Three values, all from the pool's Overview page and its **Applications → App clients** tab.
`appsettings.Development.json` is committed and holds placeholders only; user secrets override it.
The JWT authority (OIDC issuer URL) is derived from region + pool id in `Program.cs`, so there's no
fourth value to keep in sync.

**3. On the app client's Login pages screen**, register the callback URL
(`http://localhost:5173/auth/callback`) and sign-out URL (`http://localhost:5173/`), and make sure
**OpenID Connect scopes** has `openid`, `email`, and `profile` all enabled — Cognito's default
selection often isn't all three, and the authorize request fails outright (`invalid_scope`) if even
one requested scope isn't permitted.

**4. Create your first user** in the Cognito console (**Users → Create user**) with self-registration
disabled. Signing in will show "Account not set up" until an `AppUser` row exists for its `sub` —
expected, reported deliberately rather than failing silently. Fix it with:

```bash
cd TimeCalculation.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run -- --bootstrap-admin you@example.com
```

This solves the actual chicken-and-egg problem: `POST /users` needs an already-authorized caller,
but the first admin in a new environment has none yet. The command looks the user up in Cognito by
email, sets `custom:role=SystemAdmin` if it isn't already, and creates the matching `AppUser` row —
see `AdminBootstrapper.cs`. It needs AWS credentials that can call `cognito-idp:AdminGetUser` and
`AdminUpdateUserAttributes` on your pool (`aws configure`, an IAM user scoped to those actions —
**not root**, which the .NET SDK's credential chain can't consume even via `aws login`'s browser
session). If the role attribute was already correct when you created the user, no fresh sign-in is
needed — otherwise sign out and back in, since an already-issued token won't pick up the change.

## The API contract (.NET → TypeScript)

Types are generated from the API's OpenAPI document; the client is a thin typed `openapi-fetch`
wrapper (`src/api/`). See `UI_PLAN.md` §3.

```bash
# 1. regenerate the OpenAPI doc from the API (only when API contracts change)
ASPNETCORE_ENVIRONMENT=Development dotnet build ../TimeCalculation.Api -t:GenerateOpenApiDocuments
# 2. regenerate src/api/schema.d.ts from it
npm run gen:api
```

`src/api/schema.d.ts` **is committed**; CI regenerates it and fails on diff, so an API change that
breaks the UI's types is a red build on the API's own PR, not a runtime surprise. The intermediate
`openapi/*.json` is a build artifact and gitignored.

## End-to-end tests

Playwright drives the **real** stack — real API, real Postgres, real Cognito login. Nothing is
stubbed.

```bash
cp .env.e2e.example .env.e2e     # then fill in a test account's credentials
npm run e2e                       # or: npm run e2e:ui  for the interactive runner
```

`playwright.config.ts` starts both the API and the Vite dev server, reusing them if they're already
running, so this works alongside a normal dev session.

**Why real auth instead of a mock.** The alternative is a test-only auth bypass in the app, and a
bypass that exists in the binary is exactly the sort of thing that eventually gets enabled somewhere
it shouldn't. The cost is that these tests need credentials and a reachable pool, so **they are not
part of the default CI job** — they're a local gate for now. To run them in CI later, supply
`E2E_EMAIL`/`E2E_PASSWORD` as repository secrets and add a job that installs Playwright browsers.

Two consequences worth knowing:

- **Playwright's usual `storageState` login-once trick doesn't apply here.** Tokens live in memory
  only (`UI_PLAN.md` §5), so there's nothing in storage to save and replay — each test signs in.
  Cognito's own session cookie keeps repeat logins within a run cheap.
- **Tests write to whatever database the API points at.** They create and delete real client records,
  so use a dedicated test account and don't point them at anything precious.

Missing credentials fail loudly rather than skipping — a skipped auth test reports green while
testing nothing.

## Scripts

| Script | What |
|---|---|
| `dev` | Vite dev server (route tree auto-generated by the plugin) |
| `build` | `tsr generate` → `tsc -b` (strict typecheck) → `vite build` |
| `typecheck` | route gen + `tsc` only |
| `lint` | oxlint |
| `gen:api` | regenerate `src/api/schema.d.ts` from the OpenAPI doc |
| `gen:routes` | regenerate `src/routeTree.gen.ts` (gitignored; also auto-run by build/dev) |
| `e2e` | Playwright end-to-end tests (needs `.env.e2e` — see above) |
| `e2e:ui` | the same tests in Playwright's interactive UI mode |
