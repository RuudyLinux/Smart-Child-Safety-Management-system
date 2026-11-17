# Project-wide AJAX and Complete Bulk Import — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every AdminWeb operation runs without full page reloads on one shared AJAX layer, large lists page on the server, the dashboard and Guardian App stay current, and Bulk Import covers every stored field plus five new categories — with no change to business rules, security or existing API contracts.

**Architecture:** Progressive enhancement of the existing ASP.NET Core MVC AdminWeb: forms keep working without JavaScript; with it, `cg-ajax.js` submits them, controllers answer a JSON envelope via `BaseAdminController` helpers, and named page regions are re-rendered in place. The API gains optional paging (`page`/`pageSize`/`sort`/`dir` + `X-Total-Count`) without changing any response body. Bulk Import extends the existing `ImportSchema` / `ImportHandler` pipeline (atomic commit unchanged). Flutter keeps `http` + `provider`.

**Tech Stack:** .NET 10, ASP.NET Core MVC + Web API, EF Core 9 (Pomelo MySQL + Npgsql), FluentValidation, xUnit, vanilla JS (no new libraries), Playwright, Flutter (`http`, `provider`).

**Spec:** `docs/superpowers/specs/2026-11-17-ajax-and-bulk-import-design.md`

## Global Constraints

- Base branch `feature/production-free` (after PR #5 auto-checkpoint is merged). One branch + one PR per phase: `feat/ajax-core`, `feat/ajax-modules`, `feat/import-fields`, `feat/import-categories`, `feat/guardian-app-async`, `chore/regression-docs`. Each PR targets `feature/production-free`; the next phase branches from the merged result.
- Business rules unchanged. DTO changes additive only. No destructive DB operations. Additive migrations only, both providers; after any model change run `dotnet ef migrations has-pending-model-changes` for MySQL and with `Database__Provider=Postgres`.
- School calendar only via `SchoolClock` / `ISchoolScheduleProvider`. Journey changes only through existing services (`RunSerializedForStudentAsync`).
- Antiforgery on every AJAX POST (`X-CSRF-TOKEN` header from `<meta name="csrf-token">`, already accepted by AdminWeb). `[Authorize]`, `AdminOnly`, rate limits, audit unchanged.
- AJAX request marker header: `X-Requested-With: fetch`. Region request header: `X-CG-Regions: a,b`.
- Envelope JSON (camelCase): `{ ok: bool, message: string|null, redirect: string|null, regions: string[]|null, errors: { [field: string]: string[] }|null }`.
- Paging: `page` ≥ 1, `pageSize` default 25, max 100; unknown `sort` → 400; without `page` the response is identical to today. Total in response header `X-Total-Count` (also in CORS exposed headers).
- Polling: dashboard 60 s, only while `document.visibilityState === 'visible'`, stops after 30 min without input; Guardian App 30 s only while screen visible and app foregrounded. No SignalR.
- Import limits unchanged: 5,000 rows; guardians 200; commit stays one transaction.
- No new JS/NuGet/pub dependencies.

## Review Focus

1. **Session expiry mid-AJAX** (cookie or API token expired while a form is open): expect the existing redirect to `/Account/Login?expired=true`, never a JSON blob or blank region. → Task 1.3 test `ExpiredSession_FetchRequest_Gets401Envelope` + Task 1.4 E2E `session expiry during ajax save redirects to sign-in`.
2. **Double click / Enter twice on a save button**: exactly one request reaches the server. → Task 1.4 E2E `double submit sends one request`.
3. **Back button / refresh after AJAX list filtering**: the URL holds search, sort, page; reload and back restore the same view. → Task 1.6 E2E `list state survives reload and back`.
4. **Region swap breaks page widgets** (tabs, modals, dropzones, confirms, relative times inside swapped HTML stop working): every `init*` must re-bind inside the new nodes, exactly once. → Task 1.2 E2E `widgets work after region refresh`.
5. **Import references an ambiguous student** (two students with same Full Name + DOB, no Admission Number): row error, never a silent pick. → Task 4.2 test `Link_AmbiguousNameAndDob_IsRowError`.

---

## Phase 0 — Integrate auto-checkpoint (PR #5) — done, merged as `34807f2`

- [x] Review `feat/auto-checkpoint` (resolver uses `SchoolClock` + `ISchoolScheduleProvider`; endpoint read-only, `AdminOnly`, scan rate limit; DI resolves the real provider).
- [x] No conflicts with `feature/production-free`; merged build OK; unit 435/435.
- [x] Added E2E `automatic checkpoint` tests to the PR branch.
- [x] Integration suite (MySQL) green on the merged result: 368/368. Postgres runs in CI on the PR.
- [x] Full Playwright suite green on the merged result: 214/214 (first run found 2 failures: an unknown-card label regression in automatic mode, fixed; a flaky dashboard count test, deflaked).
- [x] CI on PR #5 green: build + unit, integration MySQL + Postgres, migration drift both providers.
- [x] Merged PR #5 with a merge commit (history kept).

---

## Phase 1 — AJAX core + Students pilot (PR `feat/ajax-core`)

**Acceptance:** every Students page (Index, Create, Details incl. guardians/photo/address/lifecycle, Edit) saves without a full reload; errors show inline; list search/filter/sort/page are server-side and in the URL; no-JS fallback still works; all existing tests + new tests green on MySQL and Postgres; E2E suite green.

### Task 1.1: Envelope and helpers in `BaseAdminController`

**Files:**
- Create: `src/ChildGuard.AdminWeb/Services/AjaxEnvelope.cs`
- Modify: `src/ChildGuard.AdminWeb/Controllers/BaseAdminController.cs`
- Create test project: `tests/ChildGuard.AdminWeb.Tests/` (xUnit, references AdminWeb; add to `ChildGuard.slnx`)
- Test: `tests/ChildGuard.AdminWeb.Tests/AjaxEnvelopeTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record AjaxEnvelope(bool Ok, string? Message, string? Redirect, IReadOnlyList<string>? Regions, IReadOnlyDictionary<string,string[]>? Errors)`
  - `static bool AjaxRequest.IsAjax(HttpRequest r)` → true when `X-Requested-With: fetch`
  - In `BaseAdminController`:
    - `IActionResult Done(string message, string? redirectUrl = null, params string[] regions)` — AJAX: 200 envelope; otherwise `TempData["Success"]=message` and redirect to `redirectUrl ?? Request.Path` (keeps `#fragment` via `redirectUrl`).
    - `IActionResult Failed(string message, int status = 400, string? redirectUrl = null)` — AJAX: envelope with that status; otherwise `TempData["Error"]` + redirect.
    - `IActionResult Invalid(ModelStateDictionary modelState, string? message = null)` — AJAX: 400 envelope with `errors` keyed by ModelState key (empty key → `""`); non-AJAX returns `null` so caller re-renders the view (caller pattern: `return Invalid(ModelState) ?? View(model);`).
    - `IActionResult FromApi<T>(ApiResult<T> result, string successMessage, string? redirectUrl = null, params string[] regions)` — maps API status (400/404/409/429/5xx) to `Failed` with `result.Error`, success to `Done`.

- [ ] **Step 1: Write failing tests** — `Done_Ajax_Returns200Envelope` (asserts `ok=true`, `message`, `regions` == `["guardians"]`, no TempData), `Done_NonAjax_RedirectsWithTempData` (`RedirectResult.Url == "/Students/Details/5#guardians"`, `TempData["Success"]`), `Failed_Ajax_UsesStatus` (409 → status 409, `ok=false`), `Invalid_Ajax_MapsModelStateKeys` (`errors["FullName"] == ["Required."]`), `Invalid_NonAjax_ReturnsNull`, `FromApi_404_IsFailed404`.
- [ ] **Step 2:** `dotnet test tests/ChildGuard.AdminWeb.Tests` → FAIL (types missing).
- [ ] **Step 3:** Implement. JSON via `Json(envelope)` with the app's existing camelCase options; set `Response.StatusCode`.
- [ ] **Step 4:** Tests PASS; `dotnet build ChildGuard.slnx -v q` 0 errors.
- [ ] **Step 5:** Commit `feat(adminweb): AJAX response envelope and controller helpers`.

### Task 1.2: `cg-ajax.js` request layer, forms, actions, regions; `app.js` initialisers take a root

**Files:**
- Create: `src/ChildGuard.AdminWeb/wwwroot/js/cg-ajax.js`
- Modify: `src/ChildGuard.AdminWeb/wwwroot/js/app.js` (every `init*` accepts `root = document`, binds only inside it, marks bound nodes `data-cg-bound` to stay idempotent; export `window.cg.init(root)`)
- Modify: `src/ChildGuard.AdminWeb/Views/Shared/_Layout.cshtml` (load `cg-ajax.js` after `app.js`; render requested regions — see Task 1.3)
- Modify: `src/ChildGuard.AdminWeb/wwwroot/css/site.css` (`.cg-region--loading` dim + spinner, `[aria-invalid=true]` field style using existing tokens)
- Test: `tests/e2e/specs/ajax-core.spec.ts`

**Interfaces:**
- Produces (on `window.cg`):
  - `cg.request(url, { method='GET', body, form, timeoutMs=30000, headers }) → Promise<{ status, ok, data, envelope }>`; adds `X-Requested-With: fetch`, `X-CSRF-TOKEN` for non-GET; aborts on timeout (`timeoutMs`, uploads 120000); retries a GET once on `TypeError` (network); maps failures to messages: offline `"You appear to be offline. Check the connection and try again."`, timeout `"The server took too long to answer. Try again."`, 401 → `location.href = '/Account/Login?expired=true'`, 403 `"You do not have permission to do that."`, 429 `"Too many requests. Wait a moment and try again."`, 5xx `"Something went wrong on the server. Try again."`.
  - `form[data-cg-ajax]` submit handling (busy state via existing submit-state code, in-flight lock, inline errors into `[data-valmsg-for=key]` + `aria-invalid`, summary into `[data-valmsg-summary]` for key `""`, toast via `CgToast.show`, then `envelope.redirect` → navigate, else `cg.refresh(envelope.regions ?? form.dataset.cgRegions)`; closes enclosing `.cg-modal` on success; `form.reset()` only if `data-cg-reset-on-success`).
  - `[data-cg-action]` buttons: `data-cg-action="url"`, optional `data-cg-confirm` (existing confirm runs first), `data-cg-regions`; POSTs empty body with token.
  - `cg.refresh(names: string|string[]) → Promise<void>`: GET `location.href` with `X-CG-Regions`, parse HTML, replace each `[data-cg-region=name]`, keep scroll position, focused element id, active tab, open `<details>`; then `cg.init(swappedNode)`.
  - Events: `document` dispatches `cg:ajax-done` `{ detail: { form, envelope } }` and `cg:regions-updated` `{ detail: { names } }`.
- Consumes: Task 1.1 envelope.

- [ ] **Step 1: Write failing E2E** in `ajax-core.spec.ts` against the Students Details guardian-link form (converted in Task 1.4, so these go green there): `widgets work after region refresh` (after an AJAX save that refreshes `guardians`, the tab switch, a confirm dialog and a modal inside the swapped region still work), `double submit sends one request` (count `request` events to the form action while double-clicking → 1), `offline save shows offline toast` (`context.setOffline(true)` → toast text above, form re-enabled).
- [ ] **Step 2:** Implement `cg-ajax.js` and the `app.js` root refactor.
- [ ] **Step 3:** Existing suite still green: `cd tests/e2e; npx playwright test` (initialiser refactor must not change behaviour).
- [ ] **Step 4:** Commit `feat(adminweb): shared AJAX layer and re-initialisable widgets`.

### Task 1.3: Region rendering and session expiry for AJAX

Regions need no server code: they are plain `data-cg-region="name"` elements, the full page is returned and the client extracts them (spec §5.2).

**Files:**
- Modify: `src/ChildGuard.AdminWeb/Services/ApiSessionExpiredFilter.cs` — treat `X-Requested-With: fetch` as non-HTML (401 envelope `{ ok:false, message:"Your session has expired. Sign in again.", redirect:"/Account/Login?expired=true" }`) even if `Accept` contains `text/html`.
- Modify: `src/ChildGuard.AdminWeb/Program.cs` — cookie auth `OnRedirectToLogin`: for AJAX requests return 401 envelope instead of 302.
- Test: `tests/ChildGuard.AdminWeb.Tests/SessionExpiryTests.cs`

**Interfaces:** Produces the 401 envelope contract used by `cg.request`.

- [ ] **Step 1:** Failing tests `ExpiredSession_FetchRequest_Gets401Envelope`, `ExpiredSession_PageNavigation_StillRedirects`, `AnonymousAjax_Gets401NotRedirect` (WebApplicationFactory over AdminWeb with a stub API handler).
- [ ] **Step 2–4:** Implement, PASS, build.
- [ ] **Step 5:** Commit `feat(adminweb): AJAX-aware session expiry`.

### Task 1.4: Convert Students write actions

**Files:**
- Modify: `src/ChildGuard.AdminWeb/Controllers/StudentsController.cs` (Create, Archive, Restore, DeletePermanently, EditAddress, Edit, UpdateStatus, AssignGuardian, UpdateGuardianLink, RemoveGuardianLink, UploadPhoto, DeletePhoto → `Done`/`Failed`/`Invalid`/`FromApi`; messages unchanged)
- Modify views: `Views/Students/Create.cshtml`, `Edit.cshtml`, `Details.cshtml`, `Shared/_AddressForm.cshtml`, `_PhotoManager.cshtml`, `_RecordLifecycle.cshtml`, `_DeleteModal.cshtml`, `_ArchivedBanner.cshtml` — add `data-cg-ajax` + `data-cg-regions`, `data-valmsg-for` spans, region wrappers: `student-header`, `guardians`, `photo`, `address`, `lifecycle`, `transport`.
- Test: `tests/e2e/specs/students.spec.ts` (update toast assertions to work with both TempData toasts and AJAX toasts — `toast(page)` helper already reads the toast container), new `tests/e2e/specs/students-ajax.spec.ts`.

**Region map (redirect targets keep today's fragment):** Create → redirect to Details (navigation). Edit → redirect to Details. EditAddress → regions `address`. Archive/Restore/UpdateStatus → `student-header`, `lifecycle`. Guardian link ops → `guardians`. Photo ops → `photo`, `student-header`. DeletePermanently → redirect `/Students`.

- [ ] **Step 1:** Failing E2E in `students-ajax.spec.ts`: `link guardian without reload` (assert `page.on('framenavigated')` count unchanged, guardian row appears, toast `"Guardian linked to this student."`), `edit validation error shows inline` (blank Full Name → error under field, no navigation), `archive updates header in place`, `photo upload refreshes avatar`, `no-JS fallback still saves` (`javaScriptEnabled:false` context → PRG + toast), `session expiry during ajax save redirects to sign-in` (clear cookies then save → URL `/Account/Login?expired=true`).
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Convert controller + views.
- [ ] **Step 4:** `npx playwright test --project=setup --project=chromium specs/students.spec.ts specs/students-ajax.spec.ts specs/ajax-core.spec.ts specs/lifecycle.spec.ts specs/records.spec.ts` → PASS.
- [ ] **Step 5:** Commit `feat(adminweb): Students pages save without reloading`.

### Task 1.5: Optional server paging on `GET /api/students` and `GET /api/guardians`

**Files:**
- Create: `src/ChildGuard.Application/Common/PageRequest.cs`
- Modify: `src/ChildGuard.Application/Interfaces/IStudentRepository.cs`, `src/ChildGuard.Infrastructure/Repositories/StudentRepository.cs`, `src/ChildGuard.Application/Services/StudentService.cs`, `src/ChildGuard.Api/Controllers/StudentsController.cs`; same four for Guardians
- Create: `src/ChildGuard.Api/Common/PagingHeaders.cs`
- Modify: `src/ChildGuard.Api/Program.cs` (CORS `.WithExposedHeaders("X-Total-Count")`)
- Test: `tests/ChildGuard.Application.Tests/PageRequestTests.cs`, `tests/ChildGuard.Api.IntegrationTests/PagedListTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record PageRequest(int Page, int PageSize, string? Sort, bool Descending)`; `static Result<PageRequest?> PageRequest.From(int? page, int? pageSize, string? sort, string? dir, IReadOnlySet<string> allowedSorts)` → null when `page` is null; error for page<1, pageSize<1 or >100, unknown sort, dir not `asc|desc`.
  - `public sealed record Paged<T>(IReadOnlyList<T> Items, int Total)`
  - Student filters record: `public sealed record StudentListFilter(string? Search, string? Grade, RecordStatus? Status, bool? HasRfid, bool? HasGuardian, bool? PresentToday)`
  - `Task<Paged<StudentListItemDto>> IStudentService.GetPageAsync(StudentListFilter filter, PageRequest page, CancellationToken ct)`; allowed sorts `name`, `grade`, `activity`, `status` (stable tiebreak `StudentId`). `PresentToday` and `activity` computed in SQL from Attendance/JourneyEvent within `SchoolClock.TodayStartUtc..TodayEndUtc` (passed in by the service, never `DateTime.Today`).
  - API: `GET /api/students?search&grade&status&hasRfid&hasGuardian&presentToday&page&pageSize&sort&dir`; without `page` → existing `GetAllAsync(search)` path untouched. With `page` → same `List<StudentListItemDto>` JSON for the page + `X-Total-Count`.
  - Guardians: `GuardianListFilter(string? Search, RecordStatus? Status)`, sorts `name`, `email`, `students`, `status`.
- [ ] **Step 1:** Failing unit tests: `From_NoPage_ReturnsNull`, `From_PageSize101_IsError`, `From_UnknownSort_IsError`.
- [ ] **Step 2:** Failing integration tests (run on both providers): `List_WithoutPage_BodyIdenticalToBefore` (compare to `GetAllAsync` JSON), `List_Page2_ReturnsSecondSliceAndTotal`, `List_SortByNameDesc_Ordered`, `List_FilterPresentToday_UsesSchoolDay`, `List_UnknownSort_400`, `List_Unauthenticated_401`.
- [ ] **Step 3:** Implement. Check indexes: `Students(FullName)`, `Students(Grade)`, `Students(Status)`, `Guardians(FullName)`; add an additive migration only for missing ones (MySQL + Postgres), then `has-pending-model-changes` both → "No changes".
- [ ] **Step 4:** `dotnet test tests/ChildGuard.Application.Tests` and integration suite (MySQL; Postgres with `CHILDGUARD_IT_PROVIDER=postgres`) → PASS.
- [ ] **Step 5:** Commit `feat(api): optional server-side paging for students and guardians`.

### Task 1.6: AJAX list on Students Index

**Files:**
- Modify: `src/ChildGuard.AdminWeb/Controllers/StudentsController.cs` (`Index(StudentListQuery q)` → calls paged API; `ViewBag.Grades` from a new lightweight call `GET /api/students/grades` — **additive endpoint**, returns distinct non-empty grades)
- Create: `src/ChildGuard.AdminWeb/Models/ViewModels/ListPageViewModel.cs` (`Items`, `Total`, `Page`, `PageSize`, `Sort`, `Dir`, `Query` dictionary for links)
- Create: `src/ChildGuard.AdminWeb/Views/Shared/_Pager.cshtml`, `_SortHeader.cshtml`, `_ListEmpty.cshtml`, `_ListError.cshtml`
- Modify: `Views/Students/Index.cshtml` (filters become a GET form `data-cg-list` with names `search, grade, status, hasRfid, hasGuardian, presentToday`; table body + pager inside region `list`; remove client-side `data-cg-table` paging for this table only)
- Modify: `cg-ajax.js` — `[data-cg-list]`: debounce 300 ms on text input, immediate on select; `history.replaceState` while typing, `pushState` on page/sort; `popstate` re-fetches; aborts previous request; skeleton rows while loading
- Add API endpoint: `GET /api/students/grades` in `src/ChildGuard.Api/Controllers/StudentsController.cs` + service/repo method `Task<List<string>> GetGradesAsync(CancellationToken)`
- Test: `tests/e2e/specs/students-list.spec.ts`, integration `StudentGradesTests`

- [ ] **Step 1:** Failing E2E: `search filters on the server` (create 30 students with `uid('ListX')`, search → only matches, count label `"N students"`), `list state survives reload and back` (page 2 + sort grade desc → reload → same rows; back → page 1), `filter by grade uses all grades not just current page`, `empty result shows empty state`, `api failure shows error state with retry` (`test.use({ allowErrors: [/status of 5/] })`, route-intercept API proxy action to 500).
- [ ] **Step 2–4:** Implement, PASS, full E2E suite green.
- [ ] **Step 5:** Commit `feat(adminweb): server-side, URL-addressable Students list`.

### Task 1.7: Phase 1 verification + PR

- [ ] `dotnet build ChildGuard.slnx -v q` 0 errors; unit, AdminWeb tests, integration (MySQL), integration (Postgres), full Playwright, all green; record counts in PR body.
- [ ] Manual check with `run` skill: Students pages in Chromium, no console errors.
- [ ] Open PR `feat/ajax-core` → `feature/production-free`; owner review before Phase 2.

---

## Phase 2 — Convert all remaining AdminWeb modules (PR `feat/ajax-modules`)

Depends on Phase 1 interfaces (`Done/Failed/Invalid/FromApi`, `data-cg-ajax`, `data-cg-action`, `data-cg-region`, `data-cg-list`, `PageRequest`, `Paged<T>`). Each task follows the Task 1.4 / 1.6 pattern: failing E2E first (save without reload, inline validation, conflict message, no-JS fallback for one form), convert controller + views, module spec green, commit.

**Acceptance:** no `RedirectToAction` remains in POST actions except inside the helpers and sign-in/sign-out; every list in the table below is server-paged; full E2E suite green; Playwright run shows zero full-page navigations for saves (asserted per module spec).

| Task | Controller / views | Regions | Server paging (additive API params) |
|---|---|---|---|
| 2.1 Guardians | `GuardiansController`, `Views/Guardians/*`, `_PhotoManager`, `_AddressForm` | `guardian-header`, `students`, `face`, `photo`, `address`, `credentials`, `lifecycle` | `/api/guardians` (Task 1.5) |
| 2.2 Transportation: drivers | `TransportationController` driver actions, `DriversController`, `Views/Transportation/Index.cshtml`, `Driver.cshtml` | `drivers`, `driver-header`, `driver-photo` | `/api/transportation/drivers` (`search,status`; sorts `name,license,joined`) |
| 2.3 Transportation: routes & stops | route + stop actions, `Route.cshtml` | `routes`, `stops`, `route-header` | `/api/transportation/routes` (`search,status`; `name`) |
| 2.4 Transportation: buses & assignments | bus + assignment actions, `Bus.cshtml` | `buses`, `bus-header`, `assignments` | `/api/transportation/buses`, `/assignments` (`busId,status`) |
| 2.5 RFID | `RfidController`, `Views/Rfid/*` | `cards`, `card-header`, `devices` | RFID cards list (`search,status`) |
| 2.6 Settings + School Time + Profile | `SettingsController`, `SchoolTimeController`, `ProfileController` | per settings card; `calendar`, `week`; `profile` | — |
| 2.7 Temporary Pickups + Verification history | `TemporaryPickupsController`, `VerificationController` (history list only; desk JS moves onto `cg.request` with identical behaviour incl. auto-checkpoint) | `pickup-header`, `history` | verification history (`from,to,result`) |
| 2.8 Read-only lists | Attendance, Audit, Reports filters, BulkImport History, Journey | `list` | attendance (`date,grade,status`), audit (`from,to,action,entity`), imports (`category`) |
| 2.9 Dashboard live regions | `DashboardController` → partial actions `Counts`, `Pickups`, `Verification`, `Activity`, `Alerts`; `Views/Dashboard/Index.cshtml` regions; `wwwroot/js/dashboard-live.js` | `dash-counts`, `dash-pickups`, `dash-verification`, `dash-activity`, `dash-alerts` | — |
| 2.10 Page scripts onto `cg.request` | `enrollment.js`, `bulk-import.js`, `verification.js`, `temporary-pickup-desk.js`, `alerts-badge.js`, `school-time.js`, `database.js` | — | — |

Task 2.9 tests (unit with fake timers in Playwright `page.clock`): `refreshes every 60s while visible`, `pauses when tab hidden` (emulate `visibilitychange`), `stops after 30 min idle and shows "Paused — click to refresh"`, `manual refresh button updates "Updated hh:mm"`.

Task 2.10 regression: existing `pickup.spec.ts` (incl. automatic checkpoint), `enrollment.spec.ts`, `bulk-import.spec.ts` unchanged and green.

---

## Phase 3 — Bulk Import: complete fields (PR `feat/import-fields`)

**Files:** `src/ChildGuard.Application/Import/ImportSchema.cs`, `ImportHandlers.cs`, `ImportRowReader.cs` (Gender, Time kinds), `ImportFileReader.cs` (header mapping), `src/ChildGuard.Application/Services/BulkImportService.cs`, `src/ChildGuard.Application/DTOs/ImportDtos.cs` (additive), `src/ChildGuard.Domain/Entities/DataImport.cs` + new `DataImportEmail` entity (additive migration, both providers), `src/ChildGuard.Infrastructure/...` hosted `CredentialEmailQueue`, AdminWeb `Views/BulkImport/*`, `wwwroot/js/bulk-import.js`; tests in `tests/ChildGuard.Application.Tests/Import*Tests.cs`, `tests/ChildGuard.Api.IntegrationTests/Import*Tests.cs`, `tests/e2e/specs/bulk-import.spec.ts`.

**Interfaces produced:**
- `ImportColumnKind` += `Gender`, `Time`; shared `AddressColumns` (keys `addressLine1, addressLine2, locality, landmark, city, district, state, postalCode, country`, headers "Address Line 1" … "Country", lengths from `AddressValidator`).
- `ImportColumn` += `IReadOnlyList<string> Synonyms` (additive; DTO gains `synonyms`).
- `ImportHeaderMapping.Propose(IReadOnlyList<string> fileHeaders, ImportCategoryDefinition def) → IReadOnlyDictionary<string,string?>` (file header → column key or null); preview DTO gains `proposedMapping`; validate/commit requests gain optional `mapping`.
- `DataImportEmail { Id, DataImportId, GuardianId, Status (Pending|Sent|Failed), Attempts, LastError, SentAt }`; `GET /api/imports/{id}` detail gains `emails[]`.

**Tasks** (each: failing tests → implement → green → commit):
- 3.1 Row reader kinds `Gender` (Male/Female/Other, M/F/O, case-insensitive) and `Time` (`HH:mm`, `h:mm tt`) — tests `Gender_ParsesShortForms`, `Time_Parses12And24h`, `Time_Invalid_IsError`.
- 3.2 Students fields: Admission Number (blank → auto as today; duplicate in file/DB → error), Section, Gender, Address×9, Pickup Stop, Drop Stop (need Bus Number; must be active stops on that bus's route) — tests per rule incl. `Student_StopWithoutBus_IsError`, `Student_StopNotOnBusRoute_IsError`, `Student_AdmissionNumberTaken_IsDuplicate`.
- 3.3 Guardians: Address×9 — tests `Guardian_AddressSaved`, `Guardian_AddressTooLong_IsError`.
- 3.4 Drivers: Employee Code (unique like single-record validator), Email, Address×9, Emergency Contact Name/Phone, License Category, License Expires On, Joined On — tests mirror `FleetValidators` rules.
- 3.5 Routes: Start Point, Destination. Buses: Registration Number, Manufacturer, Model — tests mirror `TransportationValidators`.
- 3.6 Templates: guidance sheet lists every column with Required/Optional, description, example; sample rows on the guidance sheet only (existing rule) — test `Template_ListsEveryColumn` for all categories.
- 3.7 Header mapping (synonyms e.g. `DOB`, `Birth Date` → dateOfBirth; `Mobile`, `Phone Number` → contactNumber) + preview UI to adjust — tests `Mapping_Synonyms`, `Mapping_UnknownHeaderIgnoredWithWarning`, E2E `upload with different headers maps and imports`.
- 3.8 Progress UI (upload `XMLHttpRequest.upload.onprogress` inside `cg.request` upload mode; validation/commit busy state with row counts).
- 3.9 Credential email queue: commit stores `DataImportEmail` rows (Pending) in the same transaction; a hosted service sends them after commit with 3 attempts; summary returns immediately; detail page shows per-guardian status — tests `GuardianImport_ReturnsBeforeEmailsSent`, `EmailFailure_RecordedPerGuardian`, `EmailQueue_ResumesPendingAfterRestart`.

**Acceptance:** every field listed in spec §6.1 importable via CSV and XLSX; existing import tests unchanged and green; migration present for both providers and `has-pending-model-changes` clean.

---

## Phase 4 — Bulk Import: new categories (PR `feat/import-categories`)

**Files:** `ImportSchema.cs` (+5 definitions), `ImportHandlers.cs` (+5 handlers registered in `DependencyInjection.cs` like the existing ones), `ImportCategory` enum (+5 values, appended), `IBulkImportRepository` write-set additions; tests as Phase 3.

**Interface:** `StudentReference.Resolve(string? admissionNumber, string? fullName, DateTime? dob, IReadOnlyList<Student> candidates) → (Student? match, string? error)`; errors `"No student has admission number {x}."`, `"No student named {name} born {dob}."`, `"{n} students are named {name} and born {dob}; use the admission number."`

**Tasks:**
- 4.1 `StudentReference` — tests `ByAdmissionNumber`, `ByNameAndDob`, `AdmissionNumberWins`, `AmbiguousNameAndDob_IsError`, `ArchivedStudent_IsError`.
- 4.2 Student–Guardian Links (Admission No. | Full Name + DOB, Guardian Email | Contact Number, Relationship, Authorized For Pickup) — reuse `StudentService` link rules — tests incl. `Link_Existing_IsDuplicate`, `Link_AmbiguousNameAndDob_IsRowError`, `Link_GuardianArchived_IsError`.
- 4.3 Route Stops (Route Name, Sequence, Stop Name, Address, Pickup Time, Drop Time) — rows grouped per route, ordered by Sequence, appended after existing stops, final numbering by the existing rule — tests `Stops_KeepFileOrder`, `Stops_AppendAfterExisting`, `Stops_DuplicateSequenceInRoute_IsError`, `Stops_UnknownRoute_IsError`.
- 4.4 Bus Assignments (student ref, Bus Number, Pickup Stop, Drop Stop) — through the existing assignment path (`TransportationService` / enrollment transaction rules) — tests `Assign_BusFull_IsError`, `Assign_AlreadyActive_IsError`, `Assign_StopOffRoute_IsError`, `Assign_CapacityCountsRowsInSameFile`.
- 4.5 RFID Cards (Card UID, optional student ref) — stock registration + optional issue via `RfidService` rules — tests `Card_Duplicate_IsError`, `Card_StudentHasActiveCard_IsError`, `Card_IssuedToStudent`.
- 4.6 School Calendar (Date, Day Type, Title, timing fields required per type) — via `SchoolCalendar` service rules — tests `Calendar_HalfDayNeedsDismissal`, `Calendar_ExistingDate_IsDuplicate`, `Calendar_InvalidType_IsError`.
- 4.7 AdminWeb category cards + templates for the five; E2E per category (CSV + XLSX), one partial-failure run with "skip invalid rows", error report download.

**Acceptance:** all five categories end to end on MySQL and Postgres; import permissions unchanged (`AdminOnly`); no business-rule change (each handler calls the same rule code as the single-record path).

---

## Phase 5 — Guardian App async (PR `feat/guardian-app-async`)

**Files:** `guardian_app/lib/core/api_client.dart` (typed `NetworkException` / `TimeoutApiException` subclasses of `ApiException`, one retry for GET on `SocketException`/`ClientException`), `lib/widgets/async_view.dart` (new, built on `cg_states.dart`), `lib/core/auto_refresh.dart` (new mixin `AutoRefresh` using `WidgetsBindingObserver` + `Timer.periodic(30s)`, paused in background/when route not current), screens: `home_tab`, `children_tab`, `student_detail_screen`, `journey_screen`, `pickups_screen`, `temporary_pickups_screen`, `notifications_screen`, `reports_screen`; push handler triggers refresh of the matching screen; tests `guardian_app/test/async_view_test.dart`, `auto_refresh_test.dart`, `api_client_test.dart`.

**Tasks:** 5.1 ApiClient errors + retry (tests with `MockClient`); 5.2 `AsyncView` (loading/empty/error-with-retry/offline widget tests); 5.3 pull-to-refresh on the eight screens (widget test per screen: drag → service called again); 5.4 `AutoRefresh` on Home and active pickup/journey (fake async: refresh at 30 s, none when paused); 5.5 mutation buttons disabled while in flight (profile save, temporary pickup create/cancel, notification read).

**Acceptance:** `flutter analyze` clean, `flutter test` green, no API contract change, web build (`flutter build web`) succeeds.

---

## Phase 6 — Regression, docs, report (PR `chore/regression-docs`)

- 6.1 Full runs: unit, AdminWeb tests, integration MySQL + Postgres, Playwright all projects (chromium, firefox, webkit smoke, responsive), `flutter test`; fix regressions at the root.
- 6.2 Docs: `docs/BULK_IMPORT.md` (categories, every field, rules, templates), `docs/ADMINWEB_AJAX.md` (envelope, attributes, regions, lists, polling), API changes in `docs/PRODUCTION_DEPLOYMENT.md` (paging params, `X-Total-Count`, new endpoints `/api/students/grades`), `CLAUDE.md` one-line invariant for the envelope helpers.
- 6.3 Final report (spec §Phase 7 items 1–8) with actual test counts.

---

## Phase dependencies

0 → 1 → 2 (needs envelope, regions, lists). 3 can start after 1 (uses `cg.request` upload mode) and runs in parallel with 2 only if the owner wants; default order 0→1→2→3→4→5→6. 4 needs 3 (`Time`/`Gender` kinds, address columns, mapping). 5 is independent of 1–4 (Flutter only) but scheduled after them. 6 last.
