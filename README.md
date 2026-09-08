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
```
start.bat
```
Opens three windows: `ChildGuard.Api` (http://localhost:5275),
`ChildGuard.AdminWeb` (http://localhost:5080), and the face service
(http://localhost:8001). Stop everything with:
```
stop.bat
```

Run the Guardian app separately (it's a mobile/web client, not a Windows service):
```
cd guardian_app
flutter run -d chrome --dart-define=API_BASE_URL=http://localhost:5275   # web
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5275              # Android emulator (default)
```

## Demo login

- **Admin Web** (http://localhost:5080): the `SeedAdmin` email/password you configured above.
- **Guardian app**: any account created via Register, or (if demo data is seeded)
  `priya.patel@demo.childguard.local` / `Guardian@123`.

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

## Development rules this project follows

See `ChildGuard_Master_Development_Task_Updated.md` — no GPS/live tracking,
no student login/app, no SOS features, RFID identifies students, face
recognition verifies guardians, single configurable RFID reader, no secrets
in source control.
