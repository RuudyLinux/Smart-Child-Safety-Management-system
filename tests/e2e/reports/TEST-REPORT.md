# ChildGuard — End-to-End Test Report

Date: 2026-11-07 (updated after the UI redesign and bug fixes; first run 2026-11-06) · Tool: Playwright 1.65.0 (+ axe-core) · Browsers: Chromium 158, Firefox, WebKit

## 1. Result

| Run | Passed | Failed | Known bug (expected fail) | Skipped |
|---|---|---|---|---|
| Final full run (all projects) | **158** | **0** | **0** | 0 |
| Repeat run (stability check, before camera capture) | 153 | 0 | 0 | 0 |

| Project | What it runs | Result |
|---|---|---|
| setup | Admin sign-in through the login page | 1 passed |
| chromium | Full functional suite (all modules, API, accessibility, performance, animations on) | 108 passed |
| firefox | `@smoke` subset (core path of every module) | 15 passed |
| webkit | `@smoke` subset | 15 passed |
| responsive | 5 viewports × (all pages layout, navigation + menus, modal) | 15 passed |
| profile | Admin password change (runs last; it changes the only admin) | 4 passed |

Also re-verified after the fixes: solution build (0 warnings, 0 errors), no pending EF model changes, unit tests 117/117, API integration tests 81/81, `flutter analyze` clean, `flutter test` 3/3.

### Fixed on 2026-11-07

| ID | Problem | Fix | Regression test |
|---|---|---|---|
| BUG-001 | Pickup at the desk could never be approved: reading the card recorded the departure first | Desk only identifies the student at pickup (`POST /api/rfid/identify`, read-only, admin only); approval stays the single writer of the departure. "Bus drop-off — pickup" removed from the desk. Approved by the owner. | `PickupDeskIdentifyTests` (4), `pickup.spec.ts` (asserts 0 departures before approval, 1 after) |
| BUG-002 | Timestamps left the API without a UTC marker (`2026-11-06T04:30:00`), so browsers and the Flutter app read UTC as local time: "x hours ago" labels and app times were 5h30 off | EF read converter re-stamps stored instants as UTC (date of birth excluded); no schema change | `TimestampSerializationTests` |
| BUG-003 | Tables that scroll sideways (e.g. Transportation at laptop width) were unreachable by keyboard (axe `scrollable-region-focusable`) | Overflowing table wrappers become labelled, focusable regions | `quality.spec.ts` › every sideways-scrolling table is reachable by keyboard |
| BUG-004 | Safari logged a console error per link: prefetch refused over plain HTTP | Hover prefetch only on HTTPS | `motion.spec.ts` (WebKit) |
| — | Two order-dependent tests (student list paging, duplicate "Add student" link on an empty roster) | Tests search / scope before asserting | — |

### Found by exploratory testing in a live browser (Playwright MCP), 2026-11-07

A full walkthrough on an isolated stack: admin sign-in, scanner key, guardian, student, link, RFID card, driver/route/bus/assignment, a real scanner tap with its device key, desk arrival, desk pickup, guardian sign-in + OTP + first-login reset + face registration through the app API, verification, approval, and every page that reports it (dashboard, logs, journey, attendance, reports), desktop and phone width.

| ID | Problem | Fix | Regression test |
|---|---|---|---|
| BUG-005 (security) | Every admin-created guardian started with the same password, `PASSWORD`; the Add-guardian page claimed it was generated | A cryptographically random 12-character password per guardian (meets the password rules, no look-alike characters), emailed as before; OTP and forced change unchanged. Approved by the owner. | `GuardianCreationTests` › different strong random password |
| BUG-006 | Admin session outlived its API token (sliding 2 h cookie vs fixed 120 min token): after 2 h every page showed "No buses yet", zeros and "Request failed (401)" | Cookie expires with the token; any API 401 signs the admin out and redirects to sign-in with "Your session expired"; background requests answer 401 and the page script redirects | `navigation.spec.ts` › expired session (verified manually with a real stale session) |
| BUG-007 | Bell showed a red alert badge with zero alerts (`display:grid` overrode `hidden`) | `[hidden]` always hides | `navigation.spec.ts` › a hidden alert badge is really hidden |
| BUG-008 | Guardian notifications and desk messages used enum names ("SchoolArrival recorded") | Plain names ("School arrival recorded") | `RfidScanTests` › plain checkpoint names |
| BUG-009 | Guardian app showed check-in time as "16:50:03.0878150" | Formatted as "4:50 PM" | `guardian_app/test/check_in_time_test.dart` |
| BUG-010 | Transportation: an assignment using the bus's own route showed "—" | Shows the bus route, labelled "bus route" | manual (MCP) |
| BUG-011 | Desk camera gave no feedback while the permission prompt was open, and the same vague message for every failure | "Waiting for camera permission…", plus specific no-camera / in-use / refused / needs-HTTPS messages | `pickup.spec.ts` › no camera on the desk computer |
| BUG-012 | WebKit could apply `autofocus` late and pull focus back to the first field mid-typing (date typed into the name) | Autofocus applied on DOM ready; test waits for it | `students.spec.ts` (repeated 3× on all browsers) |
| — | "1 students"; bus number wrapping mid-word | Pluralised; bus number kept on one line | manual (MCP) |
| BUG-013 | Student photo not saved: a 1.6 MB upload exceeded MySQL `max_allowed_packet` (XAMPP default 1 MB); API answered 500, admin saw "An unexpected error occurred" | Photos are resized in the browser before upload (longest side 1024 px, JPEG 85%); guardian app picker capped at 1024 px; a photo the database still refuses gets a clear 413 "too large" message | `PhotoUploadLimitTests`; `students.spec.ts` › a large phone photo is resized, saved and shown |
| BUG-014 | Transportation: students whose bus assignment had ended could never be assigned again; ended assignments still used seats | Only active assignments count | `transportation.spec.ts` › change a student's bus, then end the assignment |

### CRUD completed (2026-11-07)

Audit of every page against the API: students, guardians, links, photos and RFID cards already had full create / read / update / deactivate. Verifications, attendance and journey events are system-recorded audit history and stay read-only. Added, all additive and without schema changes (records are deactivated, never deleted):

| Area | Added | Tests |
|---|---|---|
| Drivers | Edit; deactivate / reactivate | `TransportAndScannerCrudTests`, `transportation.spec.ts` |
| Routes | Edit (unique name); deactivate / reactivate | same |
| Buses | Edit number, capacity, driver, route (current values preselected); take out of / return to service | same |
| Bus assignments | Change bus (old assignment kept as history); end assignment | same |
| Checkpoint scanners | Add scanner; rename (unique label) | `TransportAndScannerCrudTests`, `rfid-settings.spec.ts` |
| Face ID (pickup verification) | Admin registers or replaces a guardian's face in person (guardian page, Photo & Face ID tab, camera). The desk no longer reports "Face did not match · 0.00" for a guardian with no registered face: capture stays off and it links to registration. Face service: match threshold fixed (similarity 0.40, was 0.68 through a distance/similarity mix-up; approved by the owner), MTCNN detection with alignment, largest face used, registration quality checks, plain-language refusals. | `FaceRegistrationTests` (7), `pickup.spec.ts` (3) |
| Student and guardian photos | "Take photo with camera": live preview, Capture, then the same preview / resize / Save step as an uploaded file. Shown only where the browser allows camera access (HTTPS or localhost). | `students.spec.ts`, `guardians.spec.ts` (fake camera) |

Inactive drivers and routes are no longer offered when adding a bus; out-of-service buses are not offered for new assignments. Not added: deactivating a scanner needs a new database column (approval required); revoking its key already stops a standalone scanner.

HTML report: `tests/e2e/playwright-report/index.html` (`npx playwright show-report` from `tests/e2e`). Machine-readable results: `tests/e2e/test-results/results.json`.

## 2. How the suite runs (and why your data is safe)

`npx playwright test` (or `tests\e2e\run-e2e.ps1`) builds and boots a **separate, throwaway stack** and removes it afterwards:

- Database `childguard_e2e_<random>` created on local MySQL, migrated, dropped at the end. The name is checked against that pattern before any CREATE/DROP, so `childguard_db` can never be touched.
- API on :5375 and Admin Web on :5180, built into `tests/e2e/.e2e/bin` (a running dev system locks the normal `bin`; the suite always tests current source).
- A random admin account and JWT secret per run. Real email and push are switched off.
- Tests sign in through the real login page and real OTP flow. No authentication or authorization check is bypassed.

## 3. Coverage

An inventory crawl (`specs/inventory.spec.ts`) lists every interactive element on every page (260 in page content plus the shared shell); the specs below exercise them by their effect, not just visibility.

| Module | Tested (outcome verified) |
|---|---|
| Authentication | Redirects when signed out; empty-field validation; wrong password message; show/hide password; sign-in; sign-out ends the session; forgot-password does not reveal accounts |
| Navigation | All 12 sidebar items (URL, single active item, heading); titles and breadcrumbs; collapse/expand rail persisted across reload, tooltips; global search (pages, `/` shortcut, student fallback, Escape); every Quick action item; account menu links; alerts bell (count, spoken label, alert centre); not-found pages and records |
| Dashboard | KPI values match the API; alert centre rows link to the right record; card links |
| Students | Create → listed → details; browser + server validation; cancel; edit; deactivate/reactivate (no hard delete); server search; client filters, clear, empty state; sort asc/desc; pagination (25/page); row actions; detail tabs and deep links; link / update / unlink guardian; photo upload, type check, preview, cancel, remove |
| Guardians | Create → listed → details; bad email / duplicate phone / short phone; edit incl. status; deactivate/reactivate; search, filters, sort, clear; tabs; linked child link; issue password (cancel and confirm), then that password really signs the guardian in; photo upload/remove |
| Attendance | Scan → row appears; search; arrival/departure filter; clear; server student filter; CSV export contents; empty future range; reversed range does not crash |
| Journey Tracking | Live board status (on bus → at school); state filter; refresh; timeline steps |
| Pickup & Drop-off | Arrival scan records checkpoint + attendance; empty and unknown card explained; duplicate scan not recorded twice; face match path; no-match path (approve locked, reject recorded); face service error; no authorised guardian; New scan reset; student photo request only when a photo exists |
| Verification Log | Approved / no-match rows and result filter |
| Transportation | Add driver, route, bus (linked); modal close by Cancel / ✕ / Escape / backdrop; required + capacity validation; assign student to bus |
| RFID | Assign card via modal, masked UID; duplicate UID refused; mark lost, status filter; details → student |
| Settings | Issue scanner key (shown once), key authenticates a real scanner heartbeat, revoke → key refused; diagnostics page runs all checks |
| Reports | Run by student; all tabs; CSV export contents; Print |
| Profile | Shows admin; wrong current password; weak password; change → new works, old refused → restored |
| API | Health; 401 for anonymous on every admin endpoint; forged token refused; JSON contracts; server validation (future DOB, empty name, unknown ids); guardian can only see own children and no admin endpoints; scan needs a device key or admin; CORS allows Admin Web only |
| Responsive | 1920×1080, 1366×768, 768×1024, 390×844, 320×568 — every page: no sideways scroll, nothing outside the viewport, sidebar footer on screen, nav scrolls; drawer on small screens; Settings reachable above the footer; all three topbar menus inside the viewport; modal fits |
| Accessibility | axe-core WCAG 2.1 A/AA, no serious/critical violations on 14 pages + sign-in; keyboard reaches main content with a visible focus ring |
| Performance | Server response < 1.5 s and load < 3 s on 5 key pages; layout shift < 0.1; JS heap stable over 20 navigations |
| Browser health | Every test fails on any console error, uncaught exception, 5xx, failed request, broken image/script/style, or missing (404) fetch |

## 4. Bugs found and fixed

| # | Severity | Bug | Root cause | Fix | Regression test |
|---|---|---|---|---|---|
| 1 | High | After cancelling a confirmation (e.g. "Issue a new password?"), the submit button stayed in its loading state and could not be pressed again | The busy-state handler ran on every `submit`, even one the confirm handler cancelled | `app.js`: busy state applied only if the event was not cancelled; confirmed forms resubmit with `requestSubmit()` (deferred, as browsers ignore it during dispatch) so they also show the spinner; spinners reset when a page is restored by Back | guardians.spec "issuing a password" |
| 2 | Medium | After linking/unlinking a guardian, uploading/removing a photo or issuing a password, the page jumped back to the Overview tab, hiding the change | Redirects dropped the tab | `StudentsController`, `GuardiansController`: redirect to `#guardians`, `#photo`, `#account` | students.spec "link, update and unlink", guardians.spec |
| 3 | Low | Pickup desk requested a photo for every scanned student, logging a 404 for each one without a photo | Desk built the photo URL unconditionally | `VerificationController.Scan` returns `studentPhotoUrl` only when the student has a photo; `verification.js` uses it | pickup.spec "makes no photo request" |
| 4 | Medium (a11y) | Dashboard had no `<h1>` | Greeting rendered as a `div` | `Dashboard/Index.cshtml`: `h1` | navigation.spec (heading on every page) |
| 5 | Serious (a11y) | Sidebar section labels failed contrast (3.7:1) on every page | `#64748B` on navy | `site.css`: `#94A3B8` (6.5:1) | quality.spec |
| 6 | Serious (a11y) | Bell and account buttons: visible text not part of their accessible name | Count badge and initials were DOM text inside labelled buttons | Badge `aria-hidden`, count spoken via `aria-label` ("Safety alerts, 3 open") in `alerts-badge.js`; account initials drawn by CSS | quality.spec, navigation.spec |
| 7 | Serious (a11y) | Success/danger text in badges, stepper, deltas below 4.5:1 | Icon/fill hues used as small text | New tokens `--cg-success-text` / `--cg-danger-text` used for text only | quality.spec |
| 8 | Serious (a11y) | Table headers, alert times and card-footer text below 4.5:1 | Muted grey on tinted backgrounds | `site.css`: `#475569` on tints, standard muted for alert times | quality.spec |
| 9 | Serious (a11y) | Links inside sentences distinguishable by colour only (Dashboard, sign-in) | No underline | `site.css`: underline links in running text | quality.spec |
| 10 | Medium (test infra) | Suite would silently test stale code while the dev system runs | Shared `bin` locked by dev processes | Stack builds to `tests/e2e/.e2e/bin` | — |

## 5. Open issues

None. BUG-001 was fixed with the owner's approval (see section 1, "Fixed on 2026-11-07"); its evidence remains in `reports/evidence/BUG-001/`.

## 6. Not covered, and why

| Area | Reason |
|---|---|
| Real face recognition | No face images in the repo; the suite uses a stub with the face service's exact HTTP contract. The real service's `/health` and the API↔service contract are exercised; matching accuracy is not. |
| Physical RFID readers | Simulated: scans go through the desk UI and the API with a real per-device key. |
| Email / push delivery | Disabled in the test stack (no messages sent); OTP read from the test API's dev-only log. |
| Guardian Flutter app UI | Not a web page; covered by `flutter test`. Its API is covered by api.spec and the integration tests. |
| Placeholder text contrast | `#8A97AB` placeholders are below 4.5:1; axe does not flag placeholders and darkening them makes empty fields look filled. Left as a design decision. |
