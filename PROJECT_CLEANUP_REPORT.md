# Project cleanup report

Date: 2026-11-12. Scope: audit, dependency review, and the low-risk cleanup you approved (batches 1 and 2).
Policy followed: zero regression. Anything not provably unused was left alone.

## 1. Backup

| Item | Value |
|---|---|
| Snapshot of all uncommitted work | branch `backup/before-project-cleanup`, commit `ddf610f` (214 files, parent = your last commit) |
| Your last commit | branch `backup/head-before-project-cleanup` |
| How it was made | a temporary Git index, so your branch, index and working tree were never touched |
| Secrets check before backing up | scanned all 214 files for secret literals and credential files: none |

Restore any removed file with `git checkout backup/before-project-cleanup -- <path>`.
No destructive Git command was run. Nothing was committed or staged by the cleanup.

## 2. Original project structure (tracked: 603 files)

| Area | Tracked files | Notes |
|---|---|---|
| `src/ChildGuard.AdminWeb` | 127 | MVC UI, `wwwroot` (css, 3 scripts, `lib/` 60 files) |
| `src/ChildGuard.Application` / `Infrastructure` / `Domain` / `Api` | 61 / 54 / 19 / 23 | clean-architecture layers |
| `guardian_app` | about 150 | Flutter app plus default platform scaffolding (android, ios, macos, linux, windows, web) |
| `face-service` | 10 plus requirements | FastAPI service |
| `tests` | 24 | unit and integration projects |

Large but git-ignored and regenerable (not part of the repository): `guardian_app/build` 3.0 GB, `face-service/.venv` 2.7 GB,
`guardian_app/.dart_tool` 341 MB, `bin/obj` about 110 MB, `tests/e2e/node_modules` 23 MB.

## 3. Files removed and why

| File(s) | Why it was safe |
|---|---|
| `src/ChildGuard.AdminWeb/wwwroot/lib/` (Bootstrap, jQuery, jQuery-validation, jQuery-validation-unobtrusive; 60 files, 9.5 MB) | Default MVC template leftovers. No view, script or stylesheet loads any file in it (checked all `Views`, `wwwroot/js`, `wwwroot/css`, `Program.cs`, `.csproj`). After removal no page requests anything under `/lib/` and no request fails. |
| `src/ChildGuard.AdminWeb/Views/Shared/_ValidationScriptsPartial.cshtml` | Referenced by no view or controller; it only pointed at the removed jQuery files. |
| `PageHeader` class in `Models/ViewModels/UiModels.cs` | Declared, referenced nowhere in C# or Razor. |

## 4. Dependencies changed

| Change | Reason |
|---|---|
| `python-multipart` removed from `face-service/requirements.txt` | The service only accepts JSON bodies (no `Form`, `File` or `UploadFile` anywhere). Verified by importing the whole app with the package blocked: all 6 POST routes register and all 92 Python tests pass. The installed virtualenv was not modified. |
| `pillow==10.4.0` added to the same file | The image-quality code imports Pillow directly; it was only present as a transitive dependency of DeepFace. Pinned to the version already installed. |

No NuGet or Flutter package was removed or upgraded.

## 5. Code optimised / refactored

Only the unused `PageHeader` class (above). No logic, naming or structure was changed.

## 6. Assets cleaned

The 9.5 MB `wwwroot/lib` folder. No images, fonts or icons were removed. All Flutter assets are in use (none are declared).

## 7. Folder structure

No folders were moved or renamed.

## 8. Security observations

- No secrets in tracked files. `appsettings*.json` contain no sensitive values. The only literal-looking hits were a secure-storage key name and a deliberately invalid dummy login on the diagnostics page.
- No vulnerable NuGet packages (including transitive).
- Startup warns (does not fail) when `Biometrics:EncryptionKey` is empty and `AllowedHosts` is `*`: set both in production.
- `Verification:RequireLiveness` is `false` by your decision: a matched photo can be approved without a live check.
- Nothing in authentication, authorisation or deployment configuration was changed.

## 9. Before and after

| Check | Before cleanup | After cleanup |
|---|---|---|
| .NET build | 0 warnings, 0 errors | 0 warnings, 0 errors |
| Unit tests | 272 pass | 272 pass |
| Integration tests (real API, throwaway MySQL) | 148 pass | 148 pass |
| Python tests | 92 pass | 92 pass (with `python-multipart` blocked) |
| Flutter | `analyze` clean, 8 tests pass | not affected by the cleanup; not re-run |
| Admin pages crawled in a real browser (19 pages) | 200 on all | 200 on all |

Pre-existing issue found and fixed first (not caused by cleanup): the integration test
`TheStoredChallenge_IsAHash_NotTheToken` still expected two head-turn steps after head turns were made optional.
One line changed in `tests/ChildGuard.Api.IntegrationTests/LayeredVerificationApiTests.cs`.

Environment note: the integration tests need the local MySQL (XAMPP) running; with it stopped they all fail, which says nothing about the code.

## 10. Features verified

Browser crawl of all 19 reachable admin pages (every sidebar page, a student detail page, a guardian detail page, Diagnostics),
original code versus cleaned code, same database:

- identical page text, identical script and stylesheet lists, identical status codes;
- identical console and network messages (the only ones are the Diagnostics page's intentional invalid-login probes, 401/400);
- no horizontal overflow at 820 px (tablet) or 390 px (phone);
- login and authenticated navigation worked;
- the 4 "broken image" counts (student and guardian detail photo slots) are identical before and after.

Not re-verified because the cleanup did not touch them: the Flutter app, the face service endpoints beyond the Python tests,
the pickup desk camera flow.

## 11. Retained because usage was uncertain or the gain is not worth the risk

| Item | Why kept |
|---|---|
| `.github/modernize/java-upgrade/` | Untracked, ignored, from a Java-upgrade assistant, irrelevant to this .NET project. Left for you to decide. |
| `cupertino_icons` (Flutter) | Template default with zero uses; removal gains nothing. |
| 35 of 388 `cg-*` CSS classes with no static reference | Class names may be built dynamically; about 2 to 3 KB to gain. |
| Duplicated JS helpers (`csrfToken`, `escapeHtml`, element builders) across `app.js`, `face-capture.js`, `face-enrol.js`, `verification.js` | Separate scripts without a module system; merging risks load-order breakage for a cosmetic gain. |
| `ChildGuard_Master_Development_Task_Updated.md` | Your original spec (per `CLAUDE.md`). |
| `tests/e2e/` (untracked, incl. a 5 MB `trace.zip`) | Your Playwright project; not touched. |
| `coverlet.collector`, `xunit` v2 | Standard test tooling. xunit v2 is flagged "legacy" in favour of v3; informational. |

## 12. Remaining recommendations

1. Restart AdminWeb (`start.bat`) to pick up the removal; the running copy still serves the old build.
2. Optional, about 3 GB: delete `guardian_app/build` and the `bin/obj` folders. They are ignored and rebuild on demand.
3. Optional: replace the stock `guardian_app/README.md` (Flutter template text) with a short real one.
4. Package updates are available (EF Core 9 to 11, FluentValidation 11 to 12, Flutter firebase packages, `intl` 0.19 to 0.20) but nothing is vulnerable;
   treat them as planned migrations, not cleanup.
5. Set `Biometrics__EncryptionKey` and a real `AllowedHosts` before production.
6. Decide whether `Verification:RequireLiveness` should stay `false` for production.
