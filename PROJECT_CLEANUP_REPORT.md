# Project cleanup report

Date: 2026-11-15. Branch: `chore/codebase-cleanup` (from `feature/production-free` at `df7bede`).
Scope: full audit of the repository and the cleanup approved by the owner (stages A to E). Policy: nothing removed
unless proven unused; no behaviour, architecture, database, API contract or UI change.

The previous cleanup (2026-11-12) left two backup branches that still exist locally:
`backup/before-project-cleanup` and `backup/head-before-project-cleanup`
(restore a file with `git checkout backup/before-project-cleanup -- <path>`).

## 1. Audit findings

| Area | Finding |
|---|---|
| Repository | 665 tracked files, 2.2 MB. No build output, logs, dumps or secrets tracked. |
| .NET packages | All 15 runtime packages used. No known vulnerabilities (direct or transitive). |
| C# types | No unused types. 28 apparent candidates are discovered at runtime (27 EF Core configurations via `ApplyConfigurationsFromAssembly`) or are extension classes (`ControllerExtensions`). |
| C# members (compiler analyzers IDE0005/0051/0052/0060/0035) | 15 unnecessary `using` directives outside migrations, 1 unused parameter, 2 EF1002 raw-SQL warnings. |
| Admin Web | Every view, partial and script referenced; every view has an action. 25 of 410 CSS classes unused. |
| Guardian app (Flutter) | Every Dart file imported; every package used except `cupertino_icons`. |
| Face service (Python) | All 8 requirements used at runtime (`uvicorn` runs the server; `cv2` is opencv-python; `PIL` is pillow). |
| Database | No pending model changes on MySQL or PostgreSQL. Migrations untouched. |

## 2. Files deleted

| File | Reason |
|---|---|
| `guardian_app/README.md` | Unmodified Flutter template ("A new Flutter project"), unchanged since the first commit; no project-specific content. The root README documents the app. |
| `PROJECT_CLEANUP_REPORT.md` (2026-11-12 version) | Superseded by this report; its only still-relevant content (the backup branches) is carried over above. |

## 3. Files modified

| File | Change |
|---|---|
| 13 C# files (Api, AdminWeb, Application, tests) | 15 unnecessary `using` directives removed (compiler-confirmed). |
| `Application/Services/VerificationSessionService.cs` | Unused `livenessScore` parameter of `ToDto` removed. The score is still stored on the verification record by `NewRecord`. |
| `Infrastructure/Repositories/TransportationRepository.cs` | Row locks rewritten as `ExecuteSqlInterpolatedAsync`, like `JourneyRepository`. Same `SELECT … FOR UPDATE` statements, id still a parameter, same lock order and transaction; clears EF1002. |
| `AdminWeb/wwwroot/css/site.css` | Rules for 25 unused classes removed, 3 rules trimmed of an unused selector, orphaned `@keyframes cg-shimmer` removed, 3 comments that described removed rules removed. |
| `guardian_app/pubspec.yaml`, `pubspec.lock` | `cupertino_icons` removed; the lockfile lost only that entry. |
| 2 test `.csproj` files | `coverlet.collector` removed. |

## 4–8. Removed code, packages and duplicates

- **NuGet:** `coverlet.collector` (both test projects). Only used for code-coverage runs; nothing runs coverage.
- **Flutter:** `cupertino_icons`. No `CupertinoIcons` use anywhere.
- **Python:** none (all used).
- **CSS classes removed:** `cg-avatar-stack`, `cg-bar` (nth-child delays), `cg-card--flush`, `cg-card--hover`, `cg-chart__label`,
  `cg-col`, `cg-dropdown__form`, `cg-dropdown__menu--left`, `cg-face-enrol`, `cg-face-enrol__view`, `cg-iconbtn--bordered`,
  `cg-modal__panel--wide`, `cg-nav__count`, `cg-nav__count--alert`, `cg-search__empty`, `cg-skeleton` (+ `--circle`, `--text`,
  `--title`), `cg-stack`, `cg-text-right`, `cg-th-sort__icon`, `cg-tick`, `cg-upload-progress`, `cg-upload-progress__fill`.
  Checked against all views, scripts and C#, including classes built in code (e.g. `'cg-badge--' + kind`).
- **Duplicates reviewed and kept:** `ReverseProxySettings` exists in both the API and AdminWeb with identical logic. AdminWeb
  cannot reference the API project and the shared Application layer has no ASP.NET dependency, so merging them would change
  the architecture.

## 9. Local build output cleaned (not in git; regenerated automatically)

`guardian_app/build` 2262 MB, `tests/e2e/.e2e` 59 MB, root `node_modules` 17 MB, `tests/e2e/test-results` and
`playwright-report` 4 MB: about 2.3 GB. Each was confirmed git-ignored with no tracked files inside.

## 10. Before and after

| Measure | Before | After |
|---|---|---|
| Tracked files | 665 | 663 |
| `site.css` | 92,404 bytes, 410 classes | 89,214 bytes, 385 classes |
| Test-project NuGet references | 9 | 7 |
| Flutter direct dependencies | 12 | 11 |
| Unnecessary `using` directives (outside migrations) | 15 | 0 |
| Build warnings (normal build) | 0 | 0 |

## 11. Verification (after every stage, and again at the end)

| Check | Result |
|---|---|
| `dotnet build` (solution) | 0 warnings, 0 errors |
| Migration drift, MySQL and PostgreSQL | No pending model changes |
| Unit tests | 356/356 |
| Integration tests (real MariaDB; includes concurrent-assignment lock tests) | 281/281 |
| Flutter `pub get`, `analyze`, `test`, `build web --release` | Clean, 21/21, web build OK |
| Face service `pip check`, runtime imports, unit tests | OK, OK, 104/104 |
| Playwright E2E (Chromium, Firefox, WebKit smoke, 5 responsive viewports, accessibility) | 163/163, after a full stack rebuild |

## 12. Kept on purpose, warnings and risks

- `neon.ts`, root `package.json` / `package-lock.json`: Neon CLI branch-policy configuration. Not used by the application; kept
  because it may be part of the database workflow. Delete together if the Neon CLI is not used.
- Explicit `Microsoft.EntityFrameworkCore(.Relational)` 9.0.19 and `MySqlConnector` 2.6.2 references: deliberate pins to patched
  versions (MySqlConnector for GHSA-473q-m89c-ghf8).
- Major upgrades available (EF Core 11, ASP.NET 11 packages, FluentValidation 12, Npgsql 10, MySqlConnector 3) are not
  compatible with the current stack (Pomelo MySQL is on EF Core 9) and are out of scope for a cleanup.
- 14 unnecessary `using` directives remain inside applied migrations, which are never edited.
- Flutter `linux/`, `macos/`, `windows/` folders kept: removing them drops platforms, a product decision.
- Android and iOS were not built locally; the only Flutter change is removing an unused font package, and the web build passed.
- Local only: `docs/ChildGuard-System-Overview.pdf` and `.claude/` are untracked and were left untouched.
