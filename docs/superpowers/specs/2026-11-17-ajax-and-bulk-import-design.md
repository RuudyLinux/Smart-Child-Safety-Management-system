# ChildGuard — Project-wide AJAX and complete Bulk Import (design)

Date: 2026-11-17 · Status: draft for approval · Approach chosen by owner: **A (progressive enhancement)**

## 1. Intent

The owner wants ChildGuard to feel like one responsive, modern system: saving, deleting, approving or
filtering anything in AdminWeb should not reload the page or lose the admin's place, and the Guardian App
should show fresh information without manual restarts. Separately, Bulk Import must cover every field the
system actually stores and every entity that genuinely benefits from bulk loading, so a school can be set
up from spreadsheets instead of hundreds of manual forms.

Success looks like:
- No AdminWeb operation does a full page reload when JavaScript is available; the admin keeps scroll,
  filters, page number and open tab after every operation.
- Lists stay fast at thousands of rows (server-side search, sort, paging).
- The dashboard and pickup desk keep themselves current.
- A school's whole roster — students with addresses, guardians, links, drivers, routes with stops, buses,
  bus assignments, RFID cards, holidays — can be loaded from templates, with row-level errors.
- Everything that works today still works, with the same security.

## 2. Goals, non-goals, anti-goals

**Goals**
- G1 Shared AJAX layer for AdminWeb (one module, one response envelope, one error/validation path).
- G2 Every AdminWeb form/action converted to it; regions refresh in place.
- G3 Server-side search/sort/paging for the large lists (students, guardians, drivers, buses, routes,
  RFID cards, audit, attendance, verification history, import history).
- G4 Dashboard sections and the alert badge refresh in place; pickup desk keeps its existing live
  behaviour on the shared layer.
- G5 Bulk Import: complete fields for the 5 existing categories + 5 new categories (§6).
- G6 Guardian App: pull-to-refresh everywhere, auto-refresh of an in-progress pickup and the dashboard,
  consistent loading/empty/error/offline states through the existing `ApiClient`.
- G7 Tests for all of the above; docs updated.

**Non-goals**
- No SPA rewrite, no new front-end framework, no SignalR.
- No new entities/tables for Classes, Sections, Academic Sessions, Staff (they do not exist today; Grade
  and Section stay free text). Admin accounts and RFID devices are not bulk-importable (security).
- No driver app (drivers have no login in the system today).
- No change to business rules (see §8 Constraints).

**Anti-goals (would be failure even if it "works")**
- AJAX that bypasses antiforgery, cookie auth, `AdminOnly` policy, rate limits or audit.
- Pages that break without JavaScript.
- Per-page copy-pasted fetch code.
- Polling that keeps Render/Neon awake around the clock or burns the free-tier hours.
- Breaking the Flutter app or any existing API client (DTO changes additive only).
- An import that half-writes a file.

## 3. Audit findings (Phase 1, summarised)

| Area | Today | Gap |
|---|---|---|
| AdminWeb forms | ~95 POST actions, Post-Redirect-Get with TempData toasts | full reloads, lost scroll/tab/filters |
| Existing AJAX | Verification desk, Enrollment, Bulk Import, Temporary Pickups, alerts badge — each with its own fetch code | not shared; inconsistent errors |
| Shared JS (`app.js`) | toasts, modals, confirm, submit busy state, client-side table filter/pager, nav progress | no request helper, no region refresh |
| Lists | API returns all rows (`/api/students?search=`, `/api/guardians` no params); browser pages them | slow at scale |
| Dashboard | rendered once; alert badge polls | sections never refresh |
| Guardian App | `http` + `provider`, 20 s/60 s timeouts, `ApiException`, `cg_states.dart` widgets | no pull-to-refresh, no pickup auto-refresh, states not uniform |
| Bulk Import | template → preview → in-browser fix → validate → atomic commit (skip-invalid option) → history → error report (CSV/XLSX); audit; 5 categories | missing fields and categories (§6) |
| Drivers | records managed in AdminWeb Transportation; no driver login | — |

## 4. Approaches considered

- **A. Progressive enhancement of the existing MVC pages (chosen).** Forms keep their `action`; a shared
  module submits them with `fetch`, the controller answers with a JSON envelope when asked, and the page
  re-renders only the marked regions by fetching the current URL and swapping them. Works without JS, keeps
  security unchanged, little per-page code.
- B. JSON endpoints + client-rendered HTML per page — duplicates every Razor template in JS; drifts.
- C. SPA rewrite — replaces working modules; ruled out by the owner's rules.

## 5. AdminWeb AJAX design

### 5.1 Response envelope (server → browser)
Every AJAX-aware AdminWeb action returns, when the request carries `X-Requested-With: fetch`:

```json
{ "ok": true, "message": "Student updated.", "redirect": null, "regions": ["student-header","guardians"], "errors": null }
{ "ok": false, "message": "Please fix the highlighted fields.", "errors": { "FullName": ["Required."] } }
```
- HTTP status mirrors the outcome: 200 ok, 400 validation, 401 session expired, 403, 404, 409 conflict,
  429, 5xx. `errors` keys are form field names (ModelState keys), so inline errors attach by `name`.
- `redirect` is set only when the resource is gone or a new page is genuinely needed (create → details,
  delete on a details page → list).
- Without the header the same action keeps its current Post-Redirect-Get behaviour (no-JS fallback).
- Implemented once in `BaseAdminController`: `Done(message, redirectAction?, regions?)`,
  `Invalid(ModelState | apiResult)`, `Failed(apiResult)`. Existing `RedirectToAction(... TempData ...)`
  calls are replaced by these helpers; the helper does PRG for non-AJAX requests.
- `ApiSessionExpiredFilter` already returns 401 JSON for fetch requests; reused.

### 5.2 Browser module `wwwroot/js/cg-ajax.js` (loaded by `_Layout`)
- `cg.request(url, {method, body, form})`: adds `X-Requested-With: fetch`, the antiforgery token
  (`X-CSRF-TOKEN` from the existing `<meta name="csrf-token">`), JSON/FormData bodies, 30 s timeout via
  `AbortController` (uploads 120 s), one retry for idempotent GETs on network failure, maps statuses to
  user messages (offline, timeout, 401 → existing session-expired flow, 403, 429, 5xx).
- `form[data-cg-ajax]`: intercepts submit; disables submitters + shows the existing busy state; blocks
  double submit (in-flight flag per form); clears and shows inline errors (`.cg-error-text`
  `[data-valmsg-for]`, `aria-invalid`); success toast via existing `toasts.show`; then refreshes
  `regions` (or the form's `data-cg-regions`) or follows `redirect`; closes the enclosing modal on success.
- Buttons/links with `data-cg-action` (archive, restore, status toggles, delete in `_DeleteModal`) post
  via the same path; existing `data-cg-confirm` dialogs run first.
- Region refresh: `[data-cg-region="name"]` elements. `cg.refresh(names)` GETs `location.href` with
  `X-CG-Regions: a,b`, the layout returns the full page (simple, cache-free), and the module swaps only
  the named regions, then re-runs the existing `init*` functions scoped to the swapped nodes
  (`cg.init(root)` — `app.js` initialisers refactored to accept a root). Scroll, focus (by id), open tab
  and open `<details>` are preserved.
- Lists: `[data-cg-list]` wraps a filter form + table region + pager. Filter input (debounced 300 ms),
  sort headers and pager links fetch the list region via GET with query string, update the URL with
  `history.replaceState`/`pushState` so back/refresh/bookmarks keep the view, and abort the previous
  in-flight list request.
- Global `cg.on('ajax:done', ...)` hook for page scripts (verification, enrollment, bulk import migrate
  onto `cg.request`; their behaviour is unchanged).
- Empty, loading (skeleton rows) and error states are partials (`_ListEmpty`, `_ListError`).

### 5.3 Pages converted
All POST actions in Students, Guardians, Transportation (drivers, routes, stops, buses, assignments),
Drivers, Rfid, Settings, SchoolTime, Profile, TemporaryPickups, Verification (approve/reject paths keep
their current JSON flow), BulkImport, Account (Forgot/Reset password; sign-in keeps a full navigation
because it sets the auth cookie and redirects). Read-only pages (Reports, Attendance, Audit, Journey,
Verification History, Import History) get AJAX filters/paging only.

### 5.4 Server-side lists (API, additive)
- Existing list endpoints accept optional `page`, `pageSize` (max 100), `sort` (whitelisted field names),
  `dir`, `search`, `status`. **Without `page` the response is byte-for-byte what it is today.** With
  `page` the body is the same array shape for that page and the total is returned in `X-Total-Count`
  (CORS-exposed). No DTO shape change, so the Flutter app and any client are unaffected.
- Implemented in the read path (`IQueryable` with `Skip/Take`, ordered by a stable key last), with
  indexes checked for each sort/filter column; any index added is a normal additive migration for both
  providers.
- AdminWeb list actions pass the query through and render the table partial + pager.

### 5.5 Dashboard and live data
- Dashboard sections become regions with their own GET partial actions (counts, today's pickups,
  verification stats, recent activity, alerts). Each refreshes every 60 s **only while the tab is
  visible** (`document.visibilityState`), with a "Updated hh:mm" label and a manual refresh button.
  Stops after 30 min of no user input to let free-tier services sleep.
- Pickup desk keeps its current cadence on `cg.request`. No other polling. No SignalR (new dependency,
  sticky connections on the free tier; not needed at this scale).

## 6. Bulk Import design

### 6.1 Field coverage for existing categories (all optional unless noted; validated by the same
rules/lengths as the single-record validators)
- **Students** (+): Admission Number (if blank, assigned automatically as today), Section, Gender
  (Male/Female/Other — mapped to the existing enum), Address (Line 1, Line 2, Locality, Landmark, City,
  District, State, Postal Code, Country), Pickup Stop, Drop Stop (stop names on the bus's route; need Bus
  Number). Existing columns unchanged.
- **Guardians** (+): Address (same 9 parts). "Guardian ID" stays the email/contact number (the system
  has no external guardian id).
- **Drivers** (+): Employee Code, Email, Address, Emergency Contact Name, Emergency Contact Phone,
  License Category, License Expires On, Joined On.
- **Routes** (+): Start Point, Destination.
- **Buses** (+): Registration Number, Manufacturer, Model.

### 6.2 New categories (existing tables, existing rules)
| Category | Columns | Rules reused |
|---|---|---|
| Route Stops | Route Name*, Sequence*, Stop Name*, Address, Pickup Time, Drop Time | route must exist and be active; rows grouped per route and appended after existing stops in the file's Sequence order; final numbering 1..n is the system's (as when adding stops by hand); duplicate Sequence or stop name within a route in the file → error |
| Student–Guardian Links | Admission Number*, Guardian Email or Contact Number*, Relationship, Authorized For Pickup | same rules as linking on the student page; an existing link → duplicate |
| Bus Assignments | Admission Number*, Bus Number*, Pickup Stop, Drop Stop | same rules as the assignment form/enrollment: bus in service, capacity, one active assignment per student, stops belong to the bus's route |
| RFID Cards | Card UID*, Admission Number | registers cards into stock; with Admission Number issues it, same rules as the RFID page (card unused, student has no active card) |
| School Calendar | Date*, Day Type* (Holiday, HalfDay, SpecialTiming, and the remaining existing `SchoolDayType` values), Title*, plus the timing fields the calendar form requires for that type (e.g. dismissal time for HalfDay) | same rules as the School Time calendar form; existing date → duplicate |
(* required)

Students are referenced by Admission Number; guardians by email or contact number; buses, routes,
drivers by their natural keys — as today, never by database ids. Admission Number is nullable and
students created before 2026-11-16 may not have one, so the student-referencing categories also accept
**Full Name + Date of Birth** (the key the student import already uses for duplicates) when Admission
Number is blank; an ambiguous match is a row error. *(Builder's call.)*

### 6.3 Workflow (existing, extended)
Select category → download template (CSV/XLSX, header row + guidance sheet with field descriptions,
required markers and sample rows) → upload → preview with row errors/warnings → fix in the browser or
re-upload → confirm (choose: all-or-nothing, or skip invalid rows) → commit → summary (inserted,
skipped, failed) → history + downloadable error report. Additions:
- **Column mapping**: when headers do not match exactly, the preview proposes a mapping (case/space/
  synonym-insensitive, e.g. "DOB" → Date of Birth) that the admin can adjust before validation.
- **Progress**: upload and validation show determinate progress; commit shows a progress state. The
  commit itself stays a single synchronous, atomic transaction (5,000-row cap; 200 for guardians because
  of credential emails), which is fast enough and keeps all-or-nothing semantics. Credential emails after
  a guardian import are already sent after commit; they move to a background queue with per-email status
  in the import detail, so a large guardian import returns quickly. No other background jobs.
- Permissions: unchanged (`AdminOnly` on the API; AdminWeb admin cookie).
- Gender accepts Male/Female/Other and M/F/O, case-insensitive. Times accept `HH:mm` and `h:mm tt`;
  dates keep the reader's existing formats. *(Builder's call.)*

## 7. Guardian App design
- Pull-to-refresh (`RefreshIndicator`) on Home, Children, Student detail, Journey, Pickups, Temporary
  pickups, Notifications, Reports.
- Auto-refresh: Home dashboard and an in-progress pickup/journey poll every 30 s while the screen is
  visible and the app is in the foreground (`WidgetsBindingObserver`), stop otherwise; push
  notifications trigger an immediate refresh of the related screen.
- One `AsyncView` widget (built on `cg_states.dart`) for loading / empty / error (with retry) / offline,
  used by every list screen. `ApiClient` gains a typed network/timeout error distinction and one retry
  for idempotent GETs. Mutations disable their button while in flight.
- No change to API contracts.

## 8. Constraints (from CLAUDE.md and the owner)
- Business rules unchanged; DTOs additive only; no destructive DB operations; additive migrations only
  for both MySQL and PostgreSQL, checked with `has-pending-model-changes`.
- School times via `SchoolClock`/`ISchoolScheduleProvider`; journey changes only via the serialized
  repository path — imports of assignments/cards must go through the same services.
- Secrets never in repo. Free-tier hosting: no always-on polling.
- Work on a branch from `origin/feature/production-free` (contains the sign-in fix); keep the solution
  building and all tests green at every phase; one PR per phase.

## 9. Delivery phases
1. AJAX core: envelope helpers, `cg-ajax.js`, region refresh, `cg.init(root)` refactor, list
   infrastructure + API paging; convert Students as the pilot.
2. Convert remaining AdminWeb modules (Guardians, Transportation/Drivers, RFID, Settings, School Time,
   Profile, Temporary Pickups, Verification, Bulk Import, Account), dashboard regions.
3. Bulk Import fields for the 5 existing categories, column mapping, progress, emailed-credential queue.
4. New import categories (Route Stops, Links, Bus Assignments, RFID Cards, School Calendar).
5. Guardian App async improvements.
6. Full regression (unit, integration on MySQL + Postgres, Playwright E2E, flutter test), docs, report.

## 10. Testing
- Unit: envelope helpers, list query builder (sort whitelist, paging bounds), every import handler
  (required/invalid types/duplicates/relations/stops order/capacity), column mapping.
- Integration (API, both providers): paged endpoints return identical bodies without `page`;
  `X-Total-Count`; each new import category end to end; atomicity on failure; permission checks.
- Playwright: every converted form (success, validation error, server error, double click, offline),
  list search/sort/page with back button, dashboard refresh, no-JS fallback smoke test.
- Flutter: widget tests for `AsyncView` states and pull-to-refresh; `flutter analyze`.

## 11. Left to the builder
Exact region names per page, debounce timings within reason, skeleton visuals (follow existing CSS
tokens), synonym list for column mapping, index choices (justify each), order of module conversion
within phase 2.
