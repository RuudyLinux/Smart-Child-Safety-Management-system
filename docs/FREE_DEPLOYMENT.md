# ChildGuard on free tiers — runbook

Cost: ₹0. Nothing here needs a paid plan, a card, or a domain.

| Piece | Where | Notes |
|---|---|---|
| API | Render web service `childguard-api` (free, Ohio) | `https://childguard-api.onrender.com` |
| Admin Web | Render web service `childguard-admin` (free, Ohio) | server-rendered, so it cannot be a GitHub Pages site |
| Database | Neon PostgreSQL 18 (free), project `sweet-bird-96372858`, default branch | Ohio (`us-east-2`), so the API runs in Render Ohio |
| Guardian app (web) | GitHub Pages, built by `.github/workflows/guardian-web-pages.yml` | `https://ruudylinux.github.io/Smart-Child-Safety-Management-system/` |
| Face service | the school's PC, public only through a Tailscale Funnel, key-protected | see limits |
| Email | Resend (sandbox) | see limits |
| Guardian app (Android) | not published | needs Firebase config and a distribution decision |

Both Render services deploy from branch `feature/production-free` with **auto-deploy off**: nothing reaches
production unless someone presses *Manual Deploy* (Render dashboard) or calls the deploy API.

## Limits to know
- **Sleep:** a free Render service stops after 15 minutes without traffic and takes about a minute to wake. The first
  request after a quiet spell is slow; AdminWeb waits up to 100 s for a sleeping API (`Api__TimeoutSeconds`).
- **Shared hours:** the Render workspace has 750 free instance-hours a month **for all its free services together**.
  This workspace also runs two other projects, so heavy use of all four can suspend them until next month.
- **Neon free plan:** 1 GB storage, 100 compute-hours a month, sleeps after 5 minutes idle, 5 GB egress, 6 hours of
  restore history, no IP allow list. Health checks use `/api/health/live` (no database) so they do not wake it.
- **Face service runs on the school's own PC, not on Render.** Render's free tier (0.1 CPU, 512 MB) cannot run it, so
  the API reaches it through a Tailscale Funnel (`docs/FACE_SERVICE_ON_THIS_PC.md`; `FaceService__BaseUrl` and
  `FaceService__ApiKey` on the API). While that PC is off, asleep or offline, face registration, live check and pickup
  face matching return an error; sign-in, students, guardians, RFID scans, journeys, attendance and reports keep
  working. Verified end to end: live API -> tunnel -> face service, a face image analysed with HTTP 200.
- **Email is sandboxed:** with Resend's test sender (`onboarding@resend.dev`), every message is redirected to the
  Resend account owner (`Email__Resend__TestRecipient`). Guardians will not receive their login codes until a domain
  is verified in Resend and `Email__From` is set to an address on it.
- **Client IP for rate limits:** `ReverseProxy__TrustAll=true` with one forwarded hop. Check in the logs that it is the
  visitor's address and not Render's edge; raise `ReverseProxy__ForwardLimit` if several proxies are in front.
- **Backups run from the school PC, not the cloud.** A public repo's Actions artifacts are world-readable, so a
  database dump must not be stored there. `scripts/backup-db.ps1` takes a `pg_dump`, checks the archive, and records a
  checksum and exact row counts; `scripts/verify-restore.ps1` restores it into a throwaway local database and compares
  the counts (tested 2026-11-13 against the live database: 26 tables, all counts matched). `scripts/install-backup-task.ps1`
  schedules it daily at 02:30 (needs `CHILDGUARD_BACKUP_CONNECTION`, see the script header). Dumps are not encrypted
  (embeddings and photos already are, by the app); keep `%USERPROFILE%\ChildGuardBackups` private, and copy it
  somewhere off the PC from time to time. Neon's own history is only 6 hours.

## Configuration (names only; values live in Render's environment, never in the repo)
**API:** `Database__Provider=Postgres`, `ConnectionStrings__ChildGuardDb` (pooled, least-privilege role `childguard_app`),
`Jwt__Secret`, `SeedAdmin__Email/Password/FullName` (first boot only), `Email__Provider=Resend`,
`Email__Resend__ApiKey`, `Email__Resend__TestRecipient`, `Biometrics__EncryptionKey` (+ `EncryptionKeyId`),
`ReverseProxy__TrustAll`, `AllowedHosts=*.onrender.com`, `Cors__AllowedOrigins__0` (the Pages origin), `School__TimeZone`.
**AdminWeb:** `Api__BaseUrl`, `Api__TimeoutSeconds`, `ReverseProxy__TrustAll`, `AllowedHosts`,
`DataProtection__KeyRingXml` (from `tools/KeyRingGenerator`), `School__TimeZone`.

Back up `Biometrics__EncryptionKey`: losing it makes stored face embeddings unreadable. After the first sign-in,
change the admin password and delete `SeedAdmin__Password`.

## Database roles
- `neondb_owner` owns the tables and applies migrations. Its password is **not** in Render.
- `childguard_app` (scoped `read_write`: SELECT/INSERT/UPDATE/DELETE on the owner's tables, now and later) is what the
  API uses. It cannot change the schema.

## Apply a migration
```
set Database__Provider=Postgres
set ConnectionStrings__ChildGuardDb=<owner, DIRECT (non-pooled) connection string>
set Jwt__Secret=design-time-only
dotnet ef database update --project src/ChildGuard.Infrastructure.Postgres --startup-project src/ChildGuard.Api
```
Do this **before** deploying the API version that needs it. Back up first.

## Deploy
Render dashboard → service → *Manual Deploy* → *Deploy latest commit* (or `POST /v1/services/{id}/deploys`).
The Guardian web app redeploys by running the *Guardian web (GitHub Pages)* workflow.

## Rotate
- Jwt secret: change `Jwt__Secret` and redeploy (signs everyone out).
- AdminWeb sessions: run `dotnet run --project tools/KeyRingGenerator`, replace `DataProtection__KeyRingXml`, redeploy.
- Database role password: `neon roles reset-password <role> --project-id <id> --branch <id>`, then update
  `ConnectionStrings__ChildGuardDb` (API) and redeploy.
