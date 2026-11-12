# ChildGuard — project notes for Claude

.NET 10 clean architecture + Flutter guardian app + Python face service. MySQL/MariaDB via EF Core 9 (Pomelo).

## Layout
- `src/ChildGuard.Domain` entities/enums · `Application` services, DTOs, validators, interfaces · `Infrastructure` EF, repos, migrations (`Persistence/Migrations`), read models (`SafetyReadService`, `DashboardService`) · `Api` controllers, auth, rate limits · `AdminWeb` MVC UI calling the API via `ApiClient`
- `tests/ChildGuard.Application.Tests` unit (fakes in `Fakes/`) · `tests/ChildGuard.Api.IntegrationTests` real API + throwaway DB
- `guardian_app/` Flutter · `face-service/` FastAPI · `docs/PRODUCTION_DEPLOYMENT.md`

## Commands
- Build: `dotnet build ChildGuard.slnx -v q`
- Unit tests: `dotnet test tests/ChildGuard.Application.Tests`
- Integration tests: `dotnet test tests/ChildGuard.Api.IntegrationTests` (needs local MySQL, root/no password by default; creates and drops `childguard_it_*`)
- New migration: `dotnet ef migrations add <Name> --project src/ChildGuard.Infrastructure --startup-project src/ChildGuard.Api --output-dir Persistence/Migrations`
- Apply: `dotnet ef database update --project src/ChildGuard.Infrastructure --startup-project src/ChildGuard.Api`
- Flutter: `cd guardian_app && flutter analyze && flutter test`
- Run all locally: `start.bat` (API :5275, AdminWeb :5080, face :8001)

## Invariants (do not break)
- Instants stored UTC. School calendar (today, day boundaries, Attendance.Date/Time, late cutoff, report ranges) only via `SchoolClock` (`School:TimeZone`, Asia/Kolkata). AdminWeb displays via `.ToSchoolTime()`.
- Journey state changes (RFID scan, pickup approval) only inside `IJourneyRepository.RunSerializedForStudentAsync` (per-student row lock). Transitions in `JourneyValidationService`.
- Pickup states derived by `GuardianVerification.GetState`: Rejected→Approved blocked, 15-min expiry, approval writes departure event + attendance in one transaction.
- History tables use RESTRICT FKs; never hard-delete students/guardians/buses, set `Status`.
- Secrets only in user-secrets/env, never in repo. Dev setup: `scripts/dev-setup-secrets.ps1`.
- Preserve existing business logic and API compatibility: DTO changes additive only (Flutter app consumes JSON); any change to business rules needs my approval first.
- No destructive database operations (DROP/DELETE/TRUNCATE, bulk data rewrites), data deletion or risky migrations without my explicit approval; back up first.
- Do not edit applied migrations; add a new one. Check `dotnet ef migrations has-pending-model-changes` after model edits.

## Skip unless asked
`bin/`, `obj/`, `guardian_app/build/`, `logs/`, `*.Designer.cs`, `ChildGuardDbContextModelSnapshot.cs`, `ChildGuard_Master_Development_Task_Updated.md` (original spec, 15 KB).
