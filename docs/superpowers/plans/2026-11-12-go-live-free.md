# Go live on free tiers (Render + Neon + GitHub) — checklist

**Goal:** ChildGuard reachable on the internet at ₹0: API and AdminWeb on Render (free web services), PostgreSQL on Neon (free), Guardian web app on GitHub Pages, email through the existing Resend account.

**Branch:** `feature/production-free` (not merged to master). Render services deploy from it with auto-deploy OFF, so every production deploy is a deliberate action.

## Constraints
- Free only. Render services must report plan `free`; nothing paid is created.
- No secrets in the repo or in logs. Secrets go to Render's environment (set through its API) and are never printed.
- Fresh production database (Neon default branch is empty). Migration is create-only, non-destructive.
- Region: Render **Ohio**, because the Neon project is in `us-east-2` (Ohio); every API request makes several queries, so they must be close.
- Face service has no free private host yet: face features are offline, everything else works.
- Resend stays in sandbox mode (mail goes to the account owner) until a domain is verified.

## Code changes needed first (all tested)
1. `GET /api/health/live`: answers without touching the database, so Render's health check does not wake Neon (free plan: 100 CU-hours a month).
2. Forwarded headers behind Render's proxy (`ReverseProxy:TrustAll`, `ForwardLimit`) in API and AdminWeb; AdminWeb cookie is `Secure`.
3. AdminWeb data-protection key ring from configuration: a free Render service loses its disk on every sleep, which would sign every admin out and break open forms.
4. Dockerfiles for API and AdminWeb (Render has no native .NET runtime); non-root; port 10000.
5. `/health` on AdminWeb.

## Deploy steps
1. Commit and push; apply `InitialPostgres` to the Neon default branch (direct URL).
2. Create two Render web services (plan free, Ohio, branch `feature/production-free`, auto-deploy off) in the "Smart child safety systems" project; set environment.
3. Deploy; check health, admin login, AdminWeb sign-in, CORS preflight.
4. GitHub Pages workflow builds the Flutter web app against the API URL; enable Pages; allow the feature branch to deploy.
5. Report URLs, what works, what does not.
