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
| `ConnectionStrings__ChildGuardDb` | yes | Dedicated DB user with rights on the ChildGuard schema only. See §3 for TLS options. |
| `Jwt__Secret` | yes | ≥ 64 random bytes. Rotating it signs every user out. |
| `Email__Resend__ApiKey` | yes | The API refuses to start outside Development without it (OTP/credential mail). |
| `FaceService__BaseUrl`, `FaceService__ApiKey` | yes | Face service must not be reachable from the internet; set `FACE_SERVICE_KEY` on that side too. |
| `Firebase__CredentialsPath` | recommended | Push notifications; without it guardians only get in-app notifications. |
| `SeedAdmin__Email`, `SeedAdmin__Password` | first boot only | Used only while the Admins table is empty. Change the password after first sign-in and remove the variables. |
| `School__TimeZone` | yes | IANA id, e.g. `Asia/Kolkata`. Must be identical in the API **and** AdminWeb. Startup fails on an unknown id. |
| `AllowedHosts` | yes | The public host name(s), not `*`. |
| `Cors__AllowedOrigins__0..n` | yes | AdminWeb origin(s) only. |
| `Rfid__DeviceApiKey` | must be empty | Deprecated shared scanner key. Use per-scanner keys (§5). |
| `SeedDemoData` | must be unset/false | Demo data is ignored outside Development anyway. |

`ASPNETCORE_ENVIRONMENT` must be `Production`. In Production the API logs a warning at
startup for: unverified database TLS, a configured shared scanner key, and
`AllowedHosts=*`. Treat any of these warnings as a deployment failure.

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

## 7. Verification before go-live

1. `dotnet test` — unit tests and API integration tests (integration tests need a local
   MySQL/MariaDB; see `tests/ChildGuard.Api.IntegrationTests/README.md`).
2. Startup log shows none of the warnings listed in §1.
3. Sign in to AdminWeb over HTTPS; scan a test card with a scanner key; approve a test
   pickup; check times shown match the school's wall clock.
