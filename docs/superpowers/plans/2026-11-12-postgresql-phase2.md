# Phase 2: PostgreSQL support (Neon free plan target) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (native, one session). Steps use checkbox syntax.

**Goal:** ChildGuard runs on PostgreSQL 18 (Neon free plan) with identical behaviour, while MySQL/MariaDB keeps working and its migrations stay untouched.

**Architecture:** `Database:Provider` (`MySql` default, `Postgres`) selects the EF provider in `AddInfrastructure`. Postgres migrations live in a new project `ChildGuard.Infrastructure.Postgres` (own snapshot, loaded by assembly name). Model differences are applied in `ChildGuardDbContext.OnModelCreating` only when the provider is Npgsql, so the MySQL snapshot does not change.

**Tech Stack:** .NET 10, EF Core 9, Npgsql.EntityFrameworkCore.PostgreSQL 9.x, Pomelo (kept), xUnit, portable PostgreSQL 18 for local tests, Neon free plan (throwaway branch) for a migration smoke test.

**Spec:** `docs/superpowers/specs/` — approved in conversation: roadmap, ₹0 constraint, Neon free plan as database, existing MySQL migrations untouched, fresh production DB (no data import).

## Global Constraints

- Existing MySQL migrations and `ChildGuardDbContextModelSnapshot.cs` are not edited. `dotnet ef migrations has-pending-model-changes` must be clean for both providers.
- DTOs/API unchanged (Flutter consumes JSON). No business-rule change.
- All 296 unit tests keep passing. Integration tests run on MySQL and on Postgres.
- Instants stay UTC; school calendar only via `SchoolClock`.
- No secrets in the repo. `.env.local` stays ignored. Never point tests at Neon's default branch or any real data.
- Free only: no paid service, no paid Neon feature.

## Review Focus

- Email/phone/card UID/bus number compared with different letter case (MySQL is case-insensitive; Postgres is not): login, uniqueness and lookups must behave the same.
- Student name search with mixed case must still match.
- A `DateTime` with `Kind=Unspecified` written to `timestamptz` (Npgsql throws) — all instants and `DateOfBirth` must round-trip.
- Duplicate unique key (RFID scan retry, verification race) must give the same API outcome on both providers, not a 500.
- Per-student row lock (`FOR UPDATE`) must serialize concurrent scans on Postgres.
- Neon connection string given as `postgresql://…?sslmode=require&channel_binding=require` must be accepted and use verified TLS.

---

### Task 0: Baseline
- [ ] Start XAMPP MariaDB, run `dotnet test tests/ChildGuard.Api.IntegrationTests`; record pass/fail count.
- [ ] Download portable PostgreSQL to `D:\Aproject\_tools\pgsql` (outside the repo), `initdb`, start on port 5433, create role for tests.

### Task 1: Provider selection and connection strings
**Files:** `src/ChildGuard.Infrastructure/DependencyInjection.cs`, `ChildGuard.Infrastructure.csproj`, new `Persistence/DatabaseProvider.cs`, test `tests/ChildGuard.Application.Tests/…` or new `ChildGuard.Infrastructure` unit tests.
**Produces:** `enum DatabaseProvider { MySql, Postgres }`; `static class PostgresConnectionString { static string Normalize(string input) }` — accepts Npgsql keyword strings unchanged, converts `postgres://`/`postgresql://` URIs (percent-decoded user/password, `sslmode`, `channel_binding`), maps `sslmode=require` to `SSL Mode=VerifyFull`, keeps `Pooling=true`, sets `Timeout=30` when absent.
- [ ] Failing tests: URI with `sslmode=require&channel_binding=require` → `SSL Mode=VerifyFull; Channel Binding=Require`; keyword string passes through; password with `@`/`%40`; unknown provider value throws at startup.
- [ ] Implement; `AddInfrastructure` switches on `Database:Provider`; Postgres branch uses `UseNpgsql(cs, o => o.MigrationsAssembly("ChildGuard.Infrastructure.Postgres"))`.
- [ ] Commit.

### Task 2: Model portability
**Files:** `Persistence/ChildGuardDbContext.cs`, configurations.
- [ ] Failing test (integration, Postgres): create guardian with `User@Example.com`, log in with `user@example.com`.
- [ ] For Npgsql only: drop explicit `LONGTEXT`/`LONGBLOB` column types (→ `text`/`bytea`); `Student.DateOfBirth` → `date`; string properties that have a unique index or are looked up by equality (`Admin.Email`, `Guardian.Email`, `Guardian.Phone`, `RfidCard.CardUid`, `RfidDevice.DeviceLabel`, `Bus.BusNumber`) → `citext`.
- [ ] UTC converter: `Unspecified` is treated as UTC on write.
- [ ] Commit.

### Task 3: SQL portability
**Files:** `Repositories/JourneyRepository.cs`, `Persistence/AttendanceTimeProjection.cs`, `Repositories/VerificationRepository.cs`, `Repositories/StudentRepository.cs`, new `Persistence/DbErrors.cs`.
**Produces:** `static class DbErrors { static bool IsUniqueViolation(Exception ex) }` (MySQL `DuplicateKeyEntry` or Postgres `23505`).
- [ ] Row lock SQL per provider (`"Students"`/`"StudentId"` quoted on Postgres).
- [ ] Legacy attendance projection SQL per provider.
- [ ] `StudentRepository` search: `EF.Functions.ILike` on Postgres (escape `%`, `_`, `\`), unchanged `Contains` on MySQL.
- [ ] Commit.

### Task 4: Postgres migrations project
**Files:** new `src/ChildGuard.Infrastructure.Postgres/` (csproj, `PostgresDesignTimeFactory.cs`, `Migrations/`), `ChildGuard.slnx`, `ChildGuard.Api.csproj` reference.
- [ ] Generate `InitialPostgres` with `dotnet ef migrations add` using the Postgres provider; review generated SQL (citext extension, `timestamptz`, `bytea`, RESTRICT FKs, unique indexes, concurrency tokens).
- [ ] `has-pending-model-changes` clean for both providers.
- [ ] Commit.

### Task 5: Integration tests on both providers
**Files:** `tests/ChildGuard.Api.IntegrationTests/Infrastructure/ApiFactory.cs`, `DatabaseAndLimitsTests.cs`, `PhotoUploadLimitTests.cs`, README.
- [ ] `CHILDGUARD_IT_PROVIDER=mysql|postgres` (default `mysql`); `CHILDGUARD_IT_POSTGRES` server string; same isolation rules (`childguard_it_<random>`, loopback only).
- [ ] Provider-specific probes (`information_schema` delete rule, packet size) per provider.
- [ ] New tests for every Review Focus line.
- [ ] Run full suite on MySQL and on Postgres; fix real differences.
- [ ] Commit.

### Task 6: CI, docs, Neon smoke test
- [ ] `.github/workflows/ci.yml`: build, unit tests, integration tests on MySQL and Postgres service containers.
- [ ] Update `CLAUDE.md` commands, `docs/PRODUCTION_DEPLOYMENT.md` database section, tests README.
- [ ] Apply the Postgres migrations to a throwaway Neon branch (not the default branch) via the direct URL, check the schema, delete the branch.
- [ ] Phase report; do not merge, do not apply to the default Neon branch.
