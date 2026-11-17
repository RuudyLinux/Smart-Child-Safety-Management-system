# ChildGuard — Production Deployment Requirements

Audience: whoever deploys and operates ChildGuard. Everything here is required unless
marked *recommended*. Development conveniences (user-secrets, XAMPP, `start.bat`,
demo data) must not be used in production.

## 1. Secrets and configuration

All secrets come from environment variables or a secret store — never from files in
the repository. ASP.NET Core maps `Section:Key` to the environment variable
`Section__Key`.

| Setting | Required | Notes |
|---|---|---|
| `Database__Provider` | optional | `MySql` (default) or `Postgres`. See §3a. |
| `ConnectionStrings__ChildGuardDb` | yes | Dedicated DB user with rights on the ChildGuard schema only. See §3 for TLS options. |
| `Jwt__Secret` | yes | ≥ 64 random bytes. Rotating it signs every user out. |
| `Email__Provider` | optional | `Resend` (default when an API key is set) or `Smtp`. `Log` is refused outside Development; an unknown value stops startup. |
| `Email__From` | yes | Must be on a domain verified with the provider. `onboarding@resend.dev` only delivers to the Resend account owner and is for testing. |
| `Email__Resend__ApiKey` | yes with Resend | The API refuses to start outside Development without a working provider (OTP/credential mail). |
| `Email__Resend__TestRecipient` | testing only | Resend account email; while `Email__From` is `@resend.dev`, all mail is redirected here. Leave unset in production. |
| `Email__Smtp__Host`, `Port`, `UseSsl`, `Username`, `Password` | with Smtp | e.g. Gmail with an app password. Secrets only. |
| `Otp__TrustedDeviceDays` | optional | Default 90. Days a phone stays OTP-exempt after its last use. `0` disables trusted phones: every sign-in needs the emailed code. Revoked on password change/reset and admin-issued passwords. |
| `Otp__LogCodeOnDeliveryFailure` | never | Ignored outside Development; the API warns at startup if it is set. Codes, keys and SMTP passwords are never logged in production. |
| `FaceService__BaseUrl`, `FaceService__ApiKey` | yes | Face service must not be reachable from the internet; set `FACE_SERVICE_KEY` on that side too. |
| `Biometrics__EncryptionKey` | strongly recommended | Base64 of 32 random bytes. New face embeddings are AES-256-GCM encrypted with it (`enc:v1:<keyId>:...`); rows written before it was set stay readable. **Without it embeddings are stored as plain JSON and the API logs a startup warning.** Keep a backup in the secret store: losing the key makes encrypted embeddings unreadable. Rotate by setting a new `Biometrics__EncryptionKey` + `Biometrics__EncryptionKeyId` and listing the old one as `Biometrics__PreviousKeys__<id>`. `Biometrics__MaxReferencesPerGuardian` (default 5) caps reference photos. |
| `Verification__RequireLiveness` | decision | Default `false`: a captured or uploaded photo that matches an authorised guardian can be approved by an administrator without the live check. Set `true` to require a passed live check for every pickup. While `false`, a held-up photo of the guardian can pass, and the original `face-verify` path also produces approvable records. A live check that was run and failed is never approvable either way. |
| `FaceVerification__*` | optional | `SessionMinutes` (10), `ChallengeSeconds` (30), `MinLiveSeconds` (2), `MaxLiveAttempts` (5), `MinFrames`/`MaxFrames` (6/16), `PassiveMinRealFraction` (0.8), `PassiveMinMeanReal` (0.6), `AmbiguityMargin` (0 = off). Thresholds are uncalibrated defaults; see `docs/face-evaluation/`. |
| `RateLimiting__FaceAnalyzePerMinute`, `RateLimiting__LiveVerifyPerMinute` | optional | Per signed-in admin; defaults 60 and 20. |
| `TemporaryPickup__MaxWindowHours`, `TemporaryPickup__MaxAdvanceDays`, `TemporaryPickup__StartGraceMinutes` | optional | Defaults 24, 14 and 10. The window can be lowered but never raised above 24 hours (hard ceiling in code). The QR token key is derived from `Jwt__Secret`, so rotating that secret also invalidates every issued temporary-pickup QR. |
| `Firebase__CredentialsPath` | recommended | Push notifications; without it guardians only get in-app notifications. |
| `SeedAdmin__Email`, `SeedAdmin__Password` | first boot only | Used only while the Admins table is empty. Change the password after first sign-in and remove the variables. |
| `School__TimeZone` | yes | IANA id, e.g. `Asia/Kolkata`. Must be identical in the API **and** AdminWeb. Startup fails on an unknown id. |
| `AllowedHosts` | yes | The public host name(s), not `*`. |
| `Cors__AllowedOrigins__0..n` | yes | AdminWeb origin(s) only. |
| `Rfid__DeviceApiKey` | must be empty | Deprecated shared scanner key. Use per-scanner keys (§5). |
| `SeedDemoData` | must be unset/false | Demo data is ignored outside Development anyway. |

`ASPNETCORE_ENVIRONMENT` must be `Production`. In Production the API logs a warning at
startup for: unverified database TLS, a missing `Biometrics__EncryptionKey`, a configured
shared scanner key, and `AllowedHosts=*`. Treat any of these warnings as a deployment failure.

## 2. HTTPS

* Terminate TLS at a reverse proxy or in Kestrel with a certificate from a trusted CA.
* The API sends HSTS outside Development and redirects HTTP to HTTPS. Serve AdminWeb and
  the API over HTTPS only; the Flutter app's `API_BASE_URL` must be `https://…` (iOS
  App Transport Security blocks plain HTTP).
* Do not expose the API's HTTP port directly to the internet.

## 3. Database

* **Engine:** MySQL 8.4 LTS or MariaDB 10.11 / 11.4 LTS. The XAMPP MariaDB 10.4 used in
  development reached end of life in June 2024 and must not be used in production.
* **TLS:** use `SslMode=VerifyFull;SslCa=<path to CA bundle>` (or `VerifyCA`) in the
  connection string. Do not rely on `VerifyFull` without `SslCa` (see
  GHSA-473q-m89c-ghf8; MySqlConnector is pinned to 2.6.2, which fixes it, but a
  configured CA is the robust setting).
* **Migrations:** apply before starting the new API version:
  `dotnet ef database update` or a generated idempotent script
  (`dotnet ef migrations script --idempotent`). Back up first.
* **Historical records:** attendance, journey events and pickup verifications are
  protected by `ON DELETE RESTRICT` foreign keys. Deactivate students/guardians/buses
  (Status), never delete them.
* **Time zone migration (one-time, automatic):** on first start after migration
  `AddSchoolTimeScanIdempotencyAndDeviceKeys`, the API converts legacy attendance rows
  (stamped in UTC by older versions) to school-local Date/Time. The original instant is
  kept in `AttendanceRecords.RecordedAtUtc`; the step only touches rows still equal to
  their UTC projection, so it cannot apply twice. Set `School__TimeZone` correctly
  **before** that first start.
* Backups: daily full + binary logs; test a restore.

### 3a. PostgreSQL (Neon free plan or self-hosted)

* Set `Database__Provider=Postgres`. `ConnectionStrings__ChildGuardDb` may be an Npgsql keyword string or a
  `postgresql://` URI. For a URI, `sslmode=require` is upgraded to `SSL Mode=VerifyFull` (the certificate is
  verified, not just encrypted). The API logs a warning at startup if TLS is not verified.
* **Engine:** PostgreSQL 16 or newer; tested on 18. The first migration creates the `citext` extension (a trusted
  extension, so the database owner can create it). `citext` keeps logins, uniqueness and list order
  case-insensitive, matching MySQL.
* **Migrations:** a separate set in `ChildGuard.Infrastructure.Postgres` (the MySQL set is untouched). Apply with the
  **direct** (non-pooled) connection string: `dotnet ef database update --project src/ChildGuard.Infrastructure.Postgres --startup-project src/ChildGuard.Api`
  with `Database__Provider=Postgres`. Back up first. Run the API itself on the **pooled** (`-pooler`) string on Neon.
  Verified on Neon (PostgreSQL 18): migration applied, and the scan/approval concurrency tests pass through both URLs.
* **Neon free plan limits (verified Nov 2026):** 1 GB storage per project, 100 CU-hours a month, compute suspends
  after 5 minutes idle (the first request after that waits a few hundred ms; a health check that touches the database
  every minute would keep it awake and use the hours up), 5 GB egress a month, 6 hours of restore history, no IP
  allow list. When storage, CU-hours or egress run out, writes fail or compute is suspended; data is not deleted.
* **Exposure:** a managed Postgres is reachable over the internet with credentials only. Keep the password in the
  secret store, rotate it if it is ever printed or shared, and never commit it. `.env*` and `.neon` are git-ignored.
* **Backups:** the 6-hour restore window is not a backup. Take scheduled `pg_dump` backups from the direct
  connection and test a restore (automated backups arrive in the security/backups phase).

## 4. Reverse proxy and rate limiting

Behind a proxy or load balancer, list it so the real client IP is used for login/OTP
rate limits and logs — otherwise every user shares the proxy's bucket:

```
ReverseProxy__KnownProxies__0=10.0.0.5
# or
ReverseProxy__KnownNetworks__0=10.0.0.0/24
```

Only listed proxies are trusted for `X-Forwarded-For`/`X-Forwarded-Proto`. Scan rate
limits are per authenticated scanner (60/min default, `RateLimiting__RfidScanPerMinute`)
and are not affected by NAT.

## 5. RFID scanners

* Each scanner gets its own key: AdminWeb → Settings → Checkpoint scanners → Issue.
  The key is shown once; configure it on the device as the `X-Device-Key` header.
  Only its SHA-256 is stored.
* A key can only scan/heartbeat as its own device. Rotate or revoke per scanner.
* Scanners should send a unique `scanId` per physical tap and reuse it on retry; the
  API then answers a retried tap with "Already recorded." instead of a failure.

## 6. Mobile app

* Android: build with a release signing key (not the debug key), set `API_BASE_URL`.
* iOS: needs a Mac build. Before release add `GoogleService-Info.plist`, the Push
  Notifications capability and `UIBackgroundModes: remote-notification`, or accept
  in-app notifications only.

## 6a. Face data and the face service

Face images, webcam frames and (decrypted) embeddings cross the API-to-face-service connection, so treat it like the database link.

* Run the face service on the same host or a private network; never expose it to the internet. Set `FACE_SERVICE_KEY` on the service and
  `FaceService__ApiKey` on the API. If the two are not on one host, put TLS in front of the face service.
* The face service needs PyTorch (`torch`, CPU build) and about 4 MB of anti-spoofing weights downloaded once from GitHub on first start
  (then it works offline). Check `GET /health`: `anti_spoofing` must be `true`. While it is `false` every live check fails (by design).
* Set `Biometrics__EncryptionKey` before registering production faces, and back the key up: a lost key makes encrypted embeddings unreadable.
  Faces registered before a key was set stay readable as plain rows until they are registered again; encrypting them is a data rewrite
  that needs an explicit decision.
* No verification photo or webcam frame is written to disk or the database; they exist in memory for one request. Audit rows
  (`FaceAuditLogs`) hold codes and scores only. No purge is configured: keep or archive audit rows per your retention policy.
* Reception staff are administrators in this system (there is no narrower role); biometric management and the verification log are visible to every admin.

## 7. Verification before go-live

1. `dotnet test` — unit tests and API integration tests (integration tests need a local
   MySQL/MariaDB; see `tests/ChildGuard.Api.IntegrationTests/README.md`).
2. Startup log shows none of the warnings listed in §1.
3. Sign in to AdminWeb over HTTPS; scan a test card with a scanner key; approve a test
   pickup; check times shown match the school's wall clock.


## Temporary pickup guardians: rollout

Adds three tables (`TemporaryPickupAuthorizations`, `TemporaryPickupPhotos`, `TemporaryPickupAuditEntries`) through migration `AddTemporaryPickupGuardians`. It only creates tables and indexes: no drops, no data rewrites, existing tables untouched; foreign keys are RESTRICT like every other history table.

1. Back up the production database first (`mysqldump --single-transaction childguard_db > backup.sql`) and keep it outside the repository.
2. Review the script: `dotnet ef migrations script <previous migration> AddTemporaryPickupGuardians --idempotent --project src/ChildGuard.Infrastructure --startup-project src/ChildGuard.Api`.
3. Apply it with the deploy account (`dotnet ef database update ...`), then deploy the API, AdminWeb and the new Guardian app build together.
4. Rollback of the schema is `Down()` (drops the three new tables, and only them); the previous API and app builds ignore the tables and keep working.

## Trusted phones (OTP once per device): rollout

Adds one table, `GuardianTrustedDevices` (migration `AddGuardianTrustedDevices`): a create-table and two indexes, nothing dropped or rewritten. Back up, review and apply it like the temporary-pickup migration, **before** deploying the new API: an API that stores trusted devices without the table fails the OTP verify step.

No existing guardian is marked trusted (that would need a bulk data change, and trust belongs to a phone, not an account). An already signed-in phone enrols itself on its next app start; every other phone asks for the emailed code once, then stops asking. Older app builds are unaffected: they never send a device token, so they keep the OTP on every sign-in. Rollback: `Otp__TrustedDeviceDays=0` switches the shortcut off at once; the migration's `Down()` drops only this table.

## Transport integrity rules: rollout

Migration `TransportIntegrityRules` adds a database key that allows only one active bus assignment per student,
a unique index on route names, `EndedAt` on assignments, the bus/route/assignment snapshot on journey events, and the
`TransportAuditEntries` table. Every change is additive, but the two unique indexes fail if existing data already breaks them.
Back up first, then run these read-only checks (MySQL); both must return 0 before applying:

```sql
SELECT COUNT(*) FROM (SELECT StudentId FROM StudentBusAssignments WHERE Status = 1 GROUP BY StudentId HAVING COUNT(*) > 1) d;
SELECT COUNT(*) FROM (SELECT TRIM(RouteName) FROM Routes GROUP BY TRIM(RouteName) HAVING COUNT(*) > 1) d;
```

If either is not 0, decide which rows to end or rename before migrating (nothing is changed automatically). Journey events
recorded before the migration have no transport snapshot; only new events carry one.

`Transport:AllowRouteOverride` (default `false`): a student's assignment always uses the bus's own route. Set it to `true`
only if your buses serve several routes; an override must then name an active route.

## School time management: rollout

Migration `SchoolTimeManagement` (separate MySQL and PostgreSQL sets) adds two tables, `SchoolTimingSettings` (the regular
week, one row) and `SchoolCalendarDays` (holidays, half days, special timings and extra working days, unique per date). It
only creates tables and one index: nothing is dropped or rewritten, and existing tables are untouched.

1. Back up, then review the script: `dotnet ef migrations script DriverPhotos SchoolTimeManagement --idempotent --project src/ChildGuard.Infrastructure --startup-project src/ChildGuard.Api`.
2. Apply it **before** deploying the new API, then deploy the API and AdminWeb together. The Guardian app needs no new build: every DTO change is an added field.
3. Until an admin saves the School Time page, the `School` configuration section supplies the regular week. New keys
   (all optional): `StartTime` (`08:00`), `DismissalTime` (`14:30`), `PickupGraceMinutes` (`30`), `WorkingDays`
   (`Monday,Tuesday,Wednesday,Thursday,Friday`). The existing `LateArrivalAfter`, `MissingCheckInAfter` and
   `AttendanceWindowDays` keep their meaning.
4. Behaviour after deploying: no absences or missing check-in alerts on weekends and holidays; the attendance rate divides by
   scheduled school days (the API field is still `SchoolDaysObserved`); past arrivals are flagged late against that date's
   cutoff; new dashboard alerts for leaving before dismissal, not being picked up after the grace period, and arrivals on a
   closed day. Scans on a closed day are still recorded, never rejected.
5. Rollback: `Down()` drops only the two new tables. The previous API ignores them.
