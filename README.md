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
  In development the OTP is not sent by SMS: it is written to the API log
  (`src/ChildGuard.Api/logs/childguard-api-*.log` and the console) as
  `Login OTP for ... (dev-only log)`.

## Guardian login flow

1. `POST /api/auth/guardian/login` — contact number + password. On success the API
   sends a 6-digit OTP to the registered contact number and returns a challenge
   handle. **No token is issued at this step.**
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
- **OTP delivery (SMS)**: same situation — no SMS gateway is in the approved
  stack, so `LoggingOtpSender` writes the login OTP to the API log instead of
  sending it. To use a real provider, implement `IOtpSender` (one method,
  `SendLoginOtpAsync`) against that provider's HTTP API and change the single
  registration in `ChildGuard.Infrastructure/DependencyInjection.cs`:

  ```csharp
  services.AddScoped<IOtpSender, LoggingOtpSender>();   // replace with your sender
  ```

  Keep provider credentials out of the repository — read them from configuration
  the same way the rest of the app does, via user-secrets in development and
  environment variables in production, e.g.:

  ```bash
  dotnet user-secrets set "Sms:BaseUrl"   "https://api.your-provider.example"
  dotnet user-secrets set "Sms:ApiKey"    "<provider api key>"
  dotnet user-secrets set "Sms:SenderId"  "CHILDGUARD"
  ```

  ```bash
  # production (environment variables — double underscore = section separator)
  Sms__BaseUrl=https://api.your-provider.example
  Sms__ApiKey=...
  Sms__SenderId=CHILDGUARD
  ```

  Never log the code once a real sender is in place.

## Development rules this project follows

See `ChildGuard_Master_Development_Task_Updated.md` — no GPS/live tracking,
no student login/app, no SOS features, RFID identifies students, face
recognition verifies guardians, single configurable RFID reader, no secrets
in source control.
