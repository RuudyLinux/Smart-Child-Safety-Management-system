# ChildGuard — Smart Child Safety Management System

School-based system for student RFID identification, attendance, transportation
journey events, guardian management, and secure guardian pickup verification
via face recognition.

## Architecture

```
Admin Web (ASP.NET MVC)     Guardian App (Flutter)
        |                           |
        └──────────┬────────────────┘
                    |
            ChildGuard.Api (ASP.NET Core Web API, JWT, RBAC)
                    |
    ┌───────────────┼────────────────────┐
    |               |                    |
  MySQL      RFID Reader Abstraction   Face Service Client
 (EF Core)                              (HTTP → Python)
                    |                    |
              Physical/Mock Reader   Python FastAPI
                                     OpenCV + DeepFace (ArcFace)
                    |
            Notification Service → Firebase FCM (pending real credentials)
```

Solution layout: `ChildGuard.Domain` → `ChildGuard.Application` →
`ChildGuard.Infrastructure` → `ChildGuard.Api`, with `ChildGuard.AdminWeb`
as an MVC client of the API (never touches MySQL directly).

## Projects

| Path | What it is |
|---|---|
| `src/ChildGuard.Domain` | Entities, enums. No dependencies. |
| `src/ChildGuard.Application` | DTOs, service interfaces/implementations, validators, business logic (incl. the RFID/journey state machine). |
| `src/ChildGuard.Infrastructure` | EF Core + MySQL, repositories, JWT/password hashing, face-service HTTP client, dev notification sender. |
| `src/ChildGuard.Api` | ASP.NET Core Web API — the only thing that talks to MySQL and the face service. |
| `src/ChildGuard.AdminWeb` | ASP.NET Core MVC admin dashboard — sidebar/header shell, students/RFID/guardians/transportation/attendance/journey/reports, and the **Student Drop-off & Pickup Verification** demo page. |
| `face-service/` | Python FastAPI service: face embedding registration + verification (OpenCV, DeepFace/ArcFace). Internal only. |
| `guardian_app/` | Flutter app for guardians — bottom nav Home / Attendance / Journey / Notifications, face registration, pickup-verification status. |

## First-time setup

> **Shortcut (Windows + XAMPP):** `powershell -File scripts\dev-setup-secrets.ps1` does
> steps 1–2 for you: it creates the database and app user with a random password and
> stores the connection string, a JWT secret and an initial admin password in
> user-secrets (nothing is printed or committed). `start.bat` runs it automatically.
> Read the values with `dotnet user-secrets list --project src\ChildGuard.Api`;
> rotate the DB password with `-RotateDbPassword`.
>
> The school's time zone is `School:TimeZone` (default `Asia/Kolkata`) in both the API
> and AdminWeb `appsettings.json`. For production see
> [docs/PRODUCTION_DEPLOYMENT.md](docs/PRODUCTION_DEPLOYMENT.md).

1. **MySQL**: create a database and a dedicated app user (don't use root):
   ```sql
   CREATE DATABASE childguard_db CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
   CREATE USER 'childguard_app'@'localhost' IDENTIFIED BY 'choose-a-password';
   GRANT ALL PRIVILEGES ON childguard_db.* TO 'childguard_app'@'localhost';
   ```
2. **Secrets** (never hardcoded — stored via `dotnet user-secrets`, scoped to `ChildGuard.Api`):
   ```
   cd src/ChildGuard.Api
   dotnet user-secrets init
   dotnet user-secrets set "ConnectionStrings:ChildGuardDb" "Server=localhost;Port=3306;Database=childguard_db;User=childguard_app;Password=...;"
   dotnet user-secrets set "Jwt:Secret" "<a long random string>"
   dotnet user-secrets set "SeedAdmin:Email" "admin@childguard.local"
   dotnet user-secrets set "SeedAdmin:Password" "<a strong password>"
   dotnet user-secrets set "SeedAdmin:FullName" "System Administrator"
   ```
3. **Apply the database schema**:
   ```
   dotnet ef database update --project src/ChildGuard.Infrastructure --startup-project src/ChildGuard.Api
   ```
   On first run the API also seeds the Admin account above, an `RFID-001` device
   row, and — in Development with `SeedDemoData: true` (see
   `appsettings.Development.json`) — a small set of demo students/guardians/buses.
4. **Face service**:
   ```
   cd face-service
   python -m venv .venv
   .venv\Scripts\pip install -r requirements.txt
   ```
5. **Guardian app**:
   ```
   cd guardian_app
   flutter pub get
   ```

## Running everything

From the repo root:
```bat
start.bat             # Starts MySQL, migrations, API, AdminWeb, Face Service & opens browser
start.bat --with-app  # Also automatically launches the Guardian Flutter Web app
```

What `start.bat` does automatically:
1. **MySQL Database**: Verifies port 3306. If stopped, it starts the `MySQL80` service, or falls back to portable XAMPP MySQL on port 3306 and configures `childguard_db`.
2. **Database Migrations**: Applies any pending EF Core database schema updates.
3. **Face Service**: Starts FastAPI / DeepFace (ArcFace) on `http://127.0.0.1:8001`.
4. **ChildGuard API**: Starts the ASP.NET Core Web API on `http://localhost:5275`.
5. **Admin Web**: Starts the MVC Admin Portal on `http://localhost:5080` and opens it in your default browser.
6. **Guardian App (Optional)**: Prompts to start the Flutter Web client on `http://localhost:5090` (or starts automatically with `--with-app`).

Stop everything with a single command:
```bat
stop.bat
```
This terminates all ChildGuard windows, safely stops portable MySQL if running, and frees ports 5275, 5080, 8001, and 5090.

## Demo login

- **Admin Web** (http://localhost:5080): the `SeedAdmin` email/password you configured above.
- **Guardian app**: guardians sign in with their **registered contact number** and
  password, then an OTP. Any account created via Register works, or (if demo data
  is seeded) contact number `9820011111` / `Guardian@123` — a default password, so
  that account is walked through the mandatory first-login password reset.
  The OTP is emailed to the guardian's registered address (see **OTP delivery**
  below). With the Resend testing sender it arrives in the Resend account's inbox,
  marked with who it was meant for.

## Guardian login flow

1. `POST /api/auth/guardian/login` — contact number + password. On success the API
   emails a 6-digit OTP to the registered email address and returns a challenge
   handle. **No token is issued at this step.**
   **Trusted phones.** The emailed code is asked once per phone. `verify-otp` with `trustDevice: true`
   returns a device secret (once, in `deviceToken`; only its SHA-256 is stored). A later `login` that sends
   that `deviceToken` with the correct password skips the OTP and returns the same outcome verify-otp would
   (`stage: Authenticated` with a session, or `PasswordResetRequired` with a ticket for a first-login
   account). A phone with no token, an unknown/revoked/expired token or a wrong password gets the normal
   OTP challenge or the normal failure. Trust is cleared when the password is changed or reset by the
   guardian, or an admin issues a password; it lapses after `Otp:TrustedDeviceDays` (default 90) without use,
   and a guardian can revoke every phone from the app (Profile → Trusted phones) or `DELETE api/me/trusted-devices`.
   An app installed before this existed trusts itself once, silently, through `POST api/me/trusted-devices`
   while it still holds a valid session. Set `Otp:TrustedDeviceDays` to 0 to turn the feature off.
2. `POST /api/auth/guardian/verify-otp` — challenge handle + code. The server then
   decides what happens next:
   - account still on an admin-supplied default password → returns a short-lived
     `resetTicket` and **no** session;
   - otherwise → returns the JWT session, and the app opens the dashboard.
3. `POST /api/auth/guardian/resend-otp` — new code for the same challenge, bounded
   by `Otp:MaxResends` and `Otp:ResendIntervalSeconds`.
4. `POST /api/auth/guardian/reset-password` — `resetTicket` + new password +
   confirmation. On success the guardian is sent back to the login page and signs
   in again with the new password (plus a fresh OTP).

Because no token exists until step 2 succeeds, the mandatory reset cannot be
skipped and `[Authorize(Policy = "GuardianOnly")]` endpoints stay unreachable
until the flow completes. An admin-set password (Guardians → Details → Login
Credentials) always flags the account for that forced reset.

OTP policy is configured under the `Otp` section of `appsettings.json`
(`CodeLength`, `ExpiryMinutes`, `MaxVerificationAttempts`, `MaxResends`,
`ResendIntervalSeconds`, `PasswordResetTicketMinutes`); any value can be
overridden per environment, e.g. `Otp__ExpiryMinutes=5`.

## Design system

Deep blue primary, light neutral background, white cards, dark navy text,
green/orange/red for success/warning/error. Shared between Admin Web
(`wwwroot/css/site.css`) and the Flutter app (`lib/core/theme.dart`) — same
tokens, same component language, no gradients or decorative UI.

## Notes on abstractions kept ready for real hardware/services

- **RFID**: a single physical reader (keyboard-wedge/USB) is expected; the
  Admin Web verification page reads scans through a focused text input, no
  simulated scanner logic. Logical checkpoint (Bus Boarding / School Arrival /
  School Departure / Bus Drop-off) is selected in the UI and validated
  server-side by `JourneyValidationService` against the student's last
  recorded event — duplicate/out-of-order scans are rejected, not recorded.
- **Face verification**: real OpenCV + DeepFace(ArcFace) service; only the
  embedding is stored, never the photo.
- **Push notifications**: real Firebase Admin SDK implementation
  (`FcmPushNotificationSender`) is wired and used by every event that notifies
  a guardian (RFID scan events, pickup approve/reject). It only activates when
  `Firebase:CredentialsPath` (a service-account JSON path, via user-secrets —
  never hardcoded) is configured and the file exists; otherwise the app falls
  back to `LoggingPushNotificationSender` automatically, so it runs fully
  without a Firebase project. On the Flutter side, run `flutterfire configure`
  to generate real `firebase_options.dart` + platform config files — the app
  registers the device's FCM token (`POST /api/me/device-token`) right after
  login, and no-ops safely if Firebase isn't configured yet.
- **Password-reset email**: no SMTP provider is in the approved stack, so the
  dev implementation logs the reset token instead of emailing it — swap
  `IPasswordResetNotifier` for a real mail sender when one is chosen.
- **OTP delivery (email)**: the login OTP is emailed through `IEmailSender`; the
  provider is chosen with `Email:Provider` and nothing else (OTP generation, expiry,
  attempts and rate limits are not affected by it).

  | `Email:Provider` | Use | Needs |
  |---|---|---|
  | `Resend` (default when an API key is set) | Resend HTTP API | `Email:Resend:ApiKey` |
  | `Smtp` | Gmail (app password) or any SMTP server | `Email:Smtp:Host`, `Port`, `UseSsl`, `Username`, `Password` |
  | `Log` | Development only: writes the email, including the code, to the log. Refused outside Development. | nothing |

  **Resend without a domain.** `onboarding@resend.dev` (the default `Email:From`) can
  only deliver to the Resend account's own address. While it is the sender, every
  message is redirected to `Email:Resend:TestRecipient` (subject `[TEST for <intended>]`
  plus a banner), and if no test recipient is set the send is refused. No request for
  another guardian's address is ever made through the testing sender. Once `Email:From`
  is on a verified domain, messages go to the real recipient and `TestRecipient` is
  ignored.

  ```bash
  dotnet user-secrets set "Email:Resend:ApiKey"        "<resend api key>" --project src/ChildGuard.Api
  dotnet user-secrets set "Email:Resend:TestRecipient" "<resend account email>" --project src/ChildGuard.Api

  # later, switching to Gmail SMTP (no code change):
  dotnet user-secrets set "Email:Provider"      "Smtp" --project src/ChildGuard.Api
  dotnet user-secrets set "Email:From"          "ChildGuard <you@gmail.com>" --project src/ChildGuard.Api
  dotnet user-secrets set "Email:Smtp:Host"     "smtp.gmail.com" --project src/ChildGuard.Api
  dotnet user-secrets set "Email:Smtp:Username" "you@gmail.com" --project src/ChildGuard.Api
  dotnet user-secrets set "Email:Smtp:Password" "<gmail app password>" --project src/ChildGuard.Api
  ```

  **Failures.** If the email cannot be sent, the login challenge is closed (resend:
  the previous code and counters are restored) and the API answers `503` with
  "We couldn't send the verification code to your email right now..."; the app shows
  that message. The provider's reason is logged server-side without the code, keys or
  passwords. `Otp:LogCodeOnDeliveryFailure` (default `false`) can write the code to the
  log to finish a local test, and only takes effect in Development.

## Temporary pickup guardians

A guardian who is authorised to collect a child can arrange for someone else (a relative, a neighbour) to collect them once, for up to 24 hours. The person is **not** a guardian: no account, no standing link, nothing that outlives the window.

1. **Request** (Guardian app → Temporary pickup): child, the person's name, contact number, relationship, a mandatory clear photo (one face) and a start/end time of at most 24 hours. Only a guardian linked to that child with pickup authority may ask; overlapping requests for one child are refused.
2. **Approval** (Admin → Temporary Pickups): an admin must approve (or reject with a reason). Approval issues a one-time QR code, shown only to the requesting guardian.
3. **Handover** (Admin → Temporary Pickups → Desk): scan the QR, compare the supplied photo with the person at the desk, capture their face. The child is released only if the QR is valid, the window is open, the child is checked in at school and the face matches the supplied photo. The School Departure event and attendance are recorded in the same transaction as the handover, under the student's journey lock.
4. **Cancel / revoke**: the requesting guardian can cancel; an admin can reject or revoke. Either clears the QR so every shared copy stops working at once.
5. **Audit and notifications**: every step, including refused scans and face mismatches, is in an append-only audit trail (Admin → Temporary Pickups → a request). Guardians are notified on request, approval, rejection, cancellation, revocation, refused attempts and handover.

Guardian app (read-only views, all scoped to the signed-in guardian's own children): dashboard, student details, transport, reports, pickup status (includes temporary pickups). API: `api/me/dashboard`, `students`, `students/{id}`, `students/{id}/report`, `pickups`, `temporary-pickups[...]`; admin: `api/temporary-pickups[...]`.

## Development rules this project follows

See `ChildGuard_Master_Development_Task_Updated.md` — no GPS/live tracking,
no student login/app, no SOS features, RFID identifies students, face
recognition verifies guardians, single configurable RFID reader, no secrets
in source control.
