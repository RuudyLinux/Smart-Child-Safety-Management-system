# Safety Monitoring: Missing Child, Left-on-Bus and Daily Timeline — Design

Status: **Draft for approval** · 2026-11-06 · Branch target: new feature branch off `feature/guardian-otp-login-and-admin-management`

## 1. Intent

Notice quickly when a child is not where they should be, either between home, bus and school or still on the bus after the route ends, and get a human to confirm the child's location fast. Show guardians each child's day as a timeline.

The governing rule, from the product owner:

> A missing RFID scan alone is never proof that a child is missing. It opens an **unconfirmed** case. Reader failures, delayed uploads and manual verification are accounted for, and unresolved cases are escalated promptly.

## 2. Goals, non-goals, anti-goals

**Goals**
- G1. Detect the four "expected scan did not happen" situations (section 5) automatically, within one monitor cycle of the deadline.
- G2. Every detection opens a **Safety Case** that follows the agreed escalation workflow: school admin, then transport staff (+2 min), then guardian (+5 min), then principal.
- G3. Distinguish a probable reader fault from a probable child problem, and say so in every alert, but **never suppress** an alert because of it.
- G4. Preserve the real time a card was scanned, even when the reader uploads it late.
- G5. Complete, append-only audit trail: who was told what, when, by which channel, who acted, and how the case ended.
- G6. Staff roles (Admin, Transport, Principal) enforced in the API (RBAC), with role-specific AdminWeb dashboards.
- G7. Daily timeline per child in the guardian app.

**Non-goals (this release)**
- Guardian-reported absences in the app (staff can mark a case "absent"). Planned next.
- Live GPS bus tracking, SMS and phone calls. Email, AdminWeb in-app alerts and guardian push only.
- School holiday calendar. A daily **"No school today" pause switch** covers it for now.

**Anti-goals (failure even if it "works")**
- A guardian being told their child is "missing" because a reader was unplugged.
- An emergency case closed by a late scan or a click without a person physically seeing the child.
- Any change to existing journey and pickup rules, existing API responses or the Flutter app's current behaviour.
- An alert storm (30 cases for one dead bus reader) that trains staff to ignore alerts.

## 3. Constraints (from CLAUDE.md and the owner)

- School calendar via `SchoolClock` only (Asia/Kolkata). Instants stored UTC.
- Journey state changes only inside `IJourneyRepository.RunSerializedForStudentAsync`. **The monitor never writes journey events.** It only reads them.
- DTO changes additive only. No changes to existing enum values or meanings.
- History tables use RESTRICT FKs and no hard deletes. Audit rows are never updated or deleted.
- New migration only, no edits to applied migrations, no data rewrites. **Back up the database before applying.**
- Secrets in user-secrets or env only.
- Every threshold in section 11 needs owner approval before implementation.

## 4. Approaches considered

| Approach | Verdict |
|---|---|
| **A. Stored Safety Cases + background monitor in the API (chosen)** | A `BackgroundService` evaluates rules every 30 s, opens or updates persisted cases, steps escalations on timers, and writes an audit log. It is the only option that can run timers and keep a trail. |
| B. Compute alerts on page load (extend today's dashboard alerts) | Rejected. It has no timers, no assignment and no audit, and nobody gets notified unless a page is open. |
| C. Detect at scan time only | Rejected. It cannot notice a scan that never happens. |

**Staff participation (owner's choice: Option 1):** add a role to existing AdminWeb accounts (Admin, Transport, Principal). Existing accounts default to Admin. Each staff member signs in and acts under their own name, so the audit trail records the real verifier. Rejected alternative: transport staff and principal as a contact list only. They could not verify in the system, so audits would name the wrong person.

## 5. Detection rules

Each rule is evaluated per active student, per school day (`SchoolClock.Today`), **skipped when** the day is paused, it is a non-school weekday per config, or a case of the same type already exists for that student and day.

"Bus rider" = student with an active `StudentBusAssignment`.

| Code | Case type | Situation | Opens when (thresholds in section 11) | Emergency? |
|---|---|---|---|---|
| R1 | `MissingCheckIn` | No boarding and no school arrival today | now > `School:MissingCheckInAfter` (09:30) | No |
| R2 | `InTransitUnconfirmed` | Bus rider scanned **BusBoarding**, no **SchoolArrival** | now > route `MorningArrivalBy` + grace, or boarding time + max ride if the route has no time | No (becomes one if verified not found) |
| R3 | `LeftOnBus` | Bus rider scanned **SchoolDeparture by RFID** (not a guardian pickup), no **BusDropoff** | now > route `AfternoonDropoffBy` + grace, or departure time + max ride | **Yes, always** |
| R4 | `NotCheckedOut` | **SchoolArrival**, no **SchoolDeparture** | now > `School:DayEndsAt` + grace | No |

**Guardian pickups are excluded from R3.** A pickup approval writes `SchoolDeparture` with `Source = Manual`. The new nullable column `JourneyEvent.GuardianVerificationId` is set by the approval path, so R3 ignores departures that have it. No business logic in the approval itself changes, only one extra column value in the same transaction.

**Existing dashboard alert.** Today's computed "has not checked in" alert is replaced by R1 cases (same 09:30 cutoff). Its tile on the dashboard stays and is fed from cases.

### 5.1 "Unconfirmed first": reader and delay evidence

Before a case opens, and on every tick while it is open, the monitor gathers **evidence** and stores it on the case (`ReaderSuspected` flag + reason text, also in the audit):

1. **Reader health.** Readers gain an optional placement: `Checkpoint` (which event they record) and `BusId` (for bus readers). A reader is **offline** when `LastSeenAt` is older than the heartbeat timeout. The monitor now also sets `ConnectionStatus = Disconnected` for stale readers (today nothing does). If the reader that should have produced the missing scan (the bus's reader for R2/R3, any school-gate reader for R1/R4) is offline or had an outage in the window, the case is marked **reader suspected**.
2. **Bus-wide pattern.** If at least the threshold share of a bus's expected riders are missing the same scan in the same window, the monitor opens **one bus-level parent case** (reader suspected) with the students as child cases. Staff see and act on one item, not 30. Each child case still has its own resolution and audit.
3. **Delayed uploads.** A scan whose reader-captured time is earlier than its receive time by more than the late-upload threshold is shown as "uploaded late". If a late scan satisfies an open case, see 5.2.

The evidence changes **wording and grouping only**. It never delays or cancels an escalation step.

### 5.2 How cases end

| Resolution | Who | Allowed for emergency? |
|---|---|---|
| `ScanReceived` (late or delayed scan satisfied the rule) | System | **No.** An emergency case moves to "Scan received, awaiting physical confirmation" and keeps escalating. |
| `SeenInPerson` (staff physically saw the child; records location, time seen, verifier) | Admin, Transport, Principal | Yes. **The only way to close an emergency.** |
| `WithGuardian` (guardian confirmed the child is with them; staff records how it was confirmed) | Admin, Principal | Yes, but only with a `SeenInPerson`-style confirmation note naming the person who saw the child |
| `MarkedAbsent` (child not attending today) | Admin, Principal | No (R1 only) |
| `ReaderFault` (false alarm caused by a reader; must also confirm the child was seen) | Admin, Principal | No |

**Escalate to emergency:** any staff member who records "verified: **not found**" turns the case into an emergency (`ConfirmedMissing`). Principal and all admins are notified immediately, whatever the timer step. Only Admin or Principal can close a `ConfirmedMissing` case, and only via `SeenInPerson`.

Every resolution needs a note. A resolved case can be **reopened** (Admin, Principal), which is audited.

## 6. Escalation workflow

Clock starts at case open (T0). Steps run **only while the case is unresolved**. Acknowledging a case does **not** stop the clock; only resolution does.

| Step | When | Who | Channel | Message tone |
|---|---|---|---|---|
| 1 | T0 | All Admin-role staff | AdminWeb alert + email | "Unconfirmed: {child} has no {expected scan}. Reader {online/offline}." |
| 2 | T0 + 2 min | Transport staff assigned to the child's bus (all Transport staff if none assigned). Case auto-assigned to them. | AdminWeb alert + email | "Please verify {child} on bus {n} in person." |
| 3 | T0 + 5 min | All active guardians linked to the child | Push (FCM) + in-app notification | "We are confirming {child}'s location. School staff are checking now." **Never says "missing".** |
| 4 | T0 + 10 min (**proposed**) | All Principal-role staff | AdminWeb alert + email | "Unresolved for 10 min." |
| E | On emergency (R3, or verified not found) | Everyone above, immediately, plus repeat to Principal and Admin every 5 min until resolved | Same, marked EMERGENCY | Guardian: "Staff are physically checking for {child} now. We will update you." |

Guardians are told the outcome when a case they were notified about is resolved ("{child} has been confirmed safe at {location}").

## 7. Data model (one new migration, additive only)

**Changed tables (new columns are nullable or defaulted, so existing rows stay valid):**

| Table | New column | Purpose |
|---|---|---|
| `Admins` | `Role` int NOT NULL DEFAULT 1 (`StaffRole.Admin`) | RBAC. Existing accounts become Admin through the column default, with no data rewrite. |
| `RfidDevices` | `Checkpoint` int NULL, `BusId` int NULL (FK Restrict) | Reader placement for reader-health evidence |
| `JourneyEvents` | `ReceivedAt` datetime NULL | Server receive time. `Timestamp` = actual scan time (unchanged for old rows). |
| `JourneyEvents` | `GuardianVerificationId` int NULL (FK Restrict) | Marks pickup departures |
| `Routes` | `MorningArrivalBy` time NULL, `AfternoonDropoffBy` time NULL | Route timetable, school-local time |

**New tables:**

- `SafetyCases`: `SafetyCaseId`, `CaseType`, `StudentId` NULL (null for bus-level parent), `BusId` NULL, `ParentCaseId` NULL, `SchoolDate` (date), `ExpectedEvent`, `ExpectedByUtc`, `Severity`, `IsEmergency`, `Status` (`Unconfirmed`, `Verifying`, `GuardianNotified`, `EscalatedToPrincipal`, `ScanReceivedAwaitingConfirmation`, `ConfirmedMissing`, `Resolved`), `ReaderSuspected`, `EvidenceSummary`, `EscalationStep`, `NextEscalationAtUtc`, `AssignedToAdminId` NULL, `OpenedAtUtc`, `ResolvedAtUtc` NULL, `ResolvedByAdminId` NULL, `ResolutionType` NULL, `ResolutionNote` NULL, `RowVersion` (optimistic concurrency).
  Unique index (`StudentId`, `SchoolDate`, `CaseType`) makes case creation idempotent. Index (`Status`, `NextEscalationAtUtc`).
- `SafetyCaseEvents` (**append-only audit**): `Id`, `SafetyCaseId` (FK Restrict), `AtUtc`, `Action` (`Opened`, `EvidenceUpdated`, `Notified`, `NotificationFailed`, `Assigned`, `Acknowledged`, `NoteAdded`, `Verified`, `Escalated`, `EmergencyDeclared`, `ScanReceived`, `Resolved`, `Reopened`), `ActorType` (`System`, `Staff`, `Device`), `ActorAdminId` NULL, `Channel` NULL, `RecipientType` and `RecipientId` NULL, `Detail` (text). No update or delete path exists in code.
- `StaffBusAssignments`: `Id`, `AdminId`, `BusId`, `Status`. Links Transport staff to buses.
- `StaffNotifications`: `Id`, `AdminId`, `SafetyCaseId` NULL, `Title`, `Message`, `IsEmergency`, `SentAtUtc`, `ReadAtUtc` NULL, `EmailStatus`, `EmailAttempts`. In-app inbox for staff. The existing `Notifications` table stays guardian-only.
- `SafetyMonitorDays`: `SchoolDate` (PK), `Paused` bool, `PausedByAdminId`, `Reason`. The "No school today" switch, audited.

All new FKs are RESTRICT.

## 8. API (all additive)

**Existing endpoints and DTOs are unchanged in shape.** Additions:

- `RfidScanRequest` gets an optional `CapturedAt` (UTC, the reader's clock). It is accepted when within the clock-skew and late-upload limits (section 11). Otherwise server time is used and the scan is flagged. `JourneyEventDto` gets optional `ReceivedAt` and `UploadedLate`.
- `LoginResponse.Role` may now be `Transport` or `Principal`. Existing admins still receive `Admin`.

**New, staff (`StaffAny` = Admin | Transport | Principal):**

```
GET  /api/safety/cases?status=open|resolved&scope=mine|all&date=
GET  /api/safety/cases/{id}                 case + full audit timeline
POST /api/safety/cases/{id}/acknowledge
POST /api/safety/cases/{id}/notes           { text }
POST /api/safety/cases/{id}/verify          { outcome: SeenInPerson|NotFound, location, seenAt, note }
POST /api/safety/cases/{id}/resolve         { resolutionType, note }      (role-checked per section 5.2)
POST /api/safety/cases/{id}/assign          { adminId }                    (Admin, Principal)
POST /api/safety/cases/{id}/reopen          { note }                       (Admin, Principal)
GET  /api/staff/me/notifications            POST /api/staff/me/notifications/{id}/read
GET  /api/safety/summary                    counts for role dashboards
```

Transport staff only see and act on cases for buses they are assigned to (all buses if they have no assignment), enforced in the query, not just the UI.

**New, Admin only:**

```
GET/PUT  /api/staff                         list staff, set role, activate/deactivate
PUT      /api/staff/{id}/buses              Transport bus assignments
PUT      /api/rfid/devices/{id}/placement   { checkpoint, busId }
PUT      /api/safety/days/{date}            { paused, reason }   (Admin, Principal)
```

Route times use the existing route update endpoint with two new optional fields.

**New, guardian (`GuardianOnly`, ownership-checked like the existing endpoints):**

```
GET /api/me/students/{id}/timeline?date=yyyy-MM-dd
```

This returns the day's ordered steps: expected → done (actual scan time, "uploaded late" flag) → pending. It also includes any case the guardian has already been told about, in guardian-safe wording (no internal evidence, no staff names). The existing `/journey` endpoint is unchanged.

## 9. Background monitor

`SafetyMonitorService : BackgroundService` in `ChildGuard.Api`, `PeriodicTimer` (30 s). Each tick runs in a new DI scope:

1. **Single runner:** take MySQL `GET_LOCK('childguard_safety_monitor', 0)`. If it is not acquired, skip the tick, so several API instances never double-notify.
2. Skip the whole tick if `Safety:Enabled = false`, today is paused, or it is not a school day.
3. Refresh reader health and mark stale readers `Disconnected` (audited on change).
4. Evaluate R1 to R4 with read-only queries, build evidence and the bus-level grouping, and insert new cases (unique index makes this idempotent across ticks and restarts).
5. Re-check open cases. If the expected scan now exists, apply 5.2 (`ScanReceived` or awaiting confirmation for emergencies).
6. Run due escalation steps (`NextEscalationAtUtc <= now`): write audit + `StaffNotifications` / guardian `Notifications` rows in one transaction, then send email/push **after commit**. Failures are written as `NotificationFailed` and retried on later ticks (max 3). A down mail server never blocks the escalation of the next step.
7. Exceptions are logged per case. One bad case never stops the tick.

Timers are wall-clock UTC instants. Cutoffs (09:30, route times) are converted through `SchoolClock`. The tick never writes journey events or attendance.

**Late-detection latency:** worst case 30 s after the deadline. A detection-to-staff-notice time under one minute is expected.

## 10. Security and RBAC

- Role claim values: `Admin` (unchanged), plus new `Transport` and `Principal`. **The existing `AdminOnly` policy is unchanged**, so Transport and Principal cannot reach student, guardian, RFID or report CRUD. New policies: `StaffAny`, `AdminOrPrincipal`.
- `OnTokenValidated` already checks the account is active per request. It will also check that the **DB role still equals the token role**, so a role change or deactivation takes effect on the next request, not at token expiry.
- AdminWeb: menu, pages and actions are filtered by role (`[Authorize(Roles=...)]` on controllers). The API stays the source of truth.
- Case actions are authorised per action (table 5.2) in the application service, not only by controller policy. The actor is always taken from the token, never from the request body.
- Guardian timeline: ownership check (existing `OwnsStudentAsync`). Internal notes, staff identities and reader details are never sent to guardians.
- `CapturedAt` from devices is bounded (skew and age limits), so a compromised reader cannot backdate events outside the school day.
- Audit rows are append-only, and there is no endpoint that edits or deletes them.
- Rate limits: case action endpoints use the existing per-identity pattern (60/min).

## 11. Business-rule thresholds — **need owner approval**

| # | Rule | Proposed value | Config key |
|---|---|---|---|
| T1 | Monitor interval | 30 s | `Safety:MonitorIntervalSeconds` |
| T2 | Reader counts as offline after no heartbeat for | 2 min | `Safety:ReaderOfflineAfterSeconds` |
| T3 | R1 Missing check-in cutoff | 09:30 (existing value) | `School:MissingCheckInAfter` |
| T4 | R2 morning grace after route `MorningArrivalBy` | 10 min | `Safety:ArrivalGraceMinutes` |
| T5 | Max ride time when a route has no timetable (R2, R3) | 75 min after boarding/departure | `Safety:MaxRideMinutes` |
| T6 | R3 afternoon grace after route `AfternoonDropoffBy` | 10 min | `Safety:DropoffGraceMinutes` |
| T7 | School day ends (for R4) + grace | 15:30 + 60 min | `School:DayEndsAt`, `Safety:CheckOutGraceMinutes` |
| T8 | Escalation: Admin / Transport / Guardian / Principal | T0 / +2 / +5 / **+10 min (proposed, you did not specify)** | `Safety:Escalation:*` |
| T9 | Emergency repeat reminders to Admin and Principal | every 5 min until resolved | `Safety:EmergencyRepeatMinutes` |
| T10 | Which cases are emergencies | R3 Left-on-bus always, plus any case verified "not found" | — |
| T11 | Bus-level grouping when missing the same scan | ≥ 50% of a bus's expected riders **and** ≥ 3 students | `Safety:BusGroupShare`, `Safety:BusGroupMinStudents` |
| T12 | Guardians of a bus-level "reader suspected" group are still notified at +5 min | **Yes** (your rule: escalate promptly), with reader-issue wording | — |
| T13 | Accept reader `CapturedAt` if | ≤ 2 min in the future, same school day, ≤ 12 h old | `Rfid:MaxClockSkewSeconds`, `Rfid:MaxUploadDelayHours` |
| T14 | Scan shown as "uploaded late" when received after capture by more than | 2 min | `Rfid:LateUploadAfterSeconds` |
| T15 | Late scan auto-resolves | Non-emergency cases only. Emergencies still need in-person confirmation. | — |
| T16 | School days | Mon–Fri. Daily pause switch for holidays. | `School:SchoolDays` |
| T17 | Failed email/push retries | 3 attempts, every tick | `Safety:NotificationRetries` |

## 12. AdminWeb

- **Safety Cases** page: live queue (refresh every 15 s), each row shows severity, child or bus, case type, reader-suspected badge, current step and **countdown to the next escalation**, and assignee.
- **Case detail**: evidence, the full audit timeline, actions (acknowledge, note, verify in person with location and time, resolve, assign, reopen) shown by role.
- **Global alert banner** on every page for unacknowledged or emergency cases (polls a lightweight summary endpoint), plus a staff notification inbox.
- **Role dashboards:** Admin sees the current dashboard plus an open-cases panel. Transport sees "My buses" (riders, who is on board now, open cases). Principal sees escalated and emergency cases plus the day's summary.
- **Staff page** (Admin): roles, activation, Transport bus assignments.
- Reader placement fields on the RFID page. Route times on the Transportation page. "No school today" switch on the dashboard (Admin, Principal).

## 13. Guardian app (Flutter)

- New **Today** timeline per child (date picker for past days) built on the existing `cg_journey_timeline.dart`. Steps: Boarded bus → Arrived at school → Left school (bus or pickup by name) → Dropped off. Each step shows the actual scan time, an "uploaded late" note when relevant, and the next expected step as pending.
- A safety notice card appears when the guardian has been notified about a case, with an outcome update when resolved.
- Guardian safety notifications use the existing `NotificationType.Journey` value, so no new enum value reaches older app builds. **Builder: confirm older app builds tolerate the new optional JSON fields** (they parse with `fromJson` and should ignore unknown keys).

## 14. Backward compatibility

- One additive migration. No column drops or renames, no data rewrites, and existing admins become `Admin` via the column default. Back up `childguard_db` first. `dotnet ef migrations has-pending-model-changes` must be clean afterwards.
- Existing endpoints, DTO shapes, enum values, journey transitions, the pickup flow and the 15-min expiry are unchanged. Existing scanners that do not send `CapturedAt` behave exactly as today.
- Feature switch `Safety:Enabled` (default **false** until you approve go-live) lets the code ship dark.
- Existing tests (114 unit, 58 integration, 39 E2E) must still pass.

## 15. Testing

**Unit (fake `TimeProvider`, fakes in `Fakes/`):**
- Each rule R1 to R4: opens at the threshold, not before. Not on paused or non-school days. Not for absent-marked students. R3 ignores pickup departures.
- Escalation fires at exactly +2, +5 and +10 min. Acknowledge does not stop it, and resolve does.
- Reader offline produces the reader-suspected flag, and escalation still proceeds.
- Bus grouping thresholds, including edge cases at 49% vs 50% and 2 vs 3 students.
- Late scan: non-emergency auto-resolves; emergency goes to awaiting confirmation.
- Resolution permission matrix (section 5.2) per role. "Not found" declares an emergency.
- `CapturedAt` bounds (future skew, too old, previous day).
- Idempotence: repeated ticks never create duplicate cases or notifications.

**Integration (real API + throwaway DB):**
- RBAC: Transport gets 403 on `/api/students`. Transport sees only its buses' cases. A role change is effective on the next request. Existing admin login still returns `Admin`.
- Scan with `CapturedAt` stores the actual time and `ReceivedAt`. Scan without it is unchanged.
- Monitor tick against seeded data creates cases and audit rows, and notifications are recorded.
- Migration on a copy of a pre-feature database: existing rows intact, admins become Admin.

**E2E (Playwright, against a seeded throwaway database, not the dev DB):** case appears in the queue, Transport verifies in person, admin resolves, and the audit timeline shows every step. Role-specific menus.

**Flutter:** widget tests for the timeline (late-upload note, pending step, safety card).

## 16. Left to the builder

- Exact query shapes and indexes beyond those listed. Keep each tick under 1 s for 2,000 students.
- AdminWeb visual details within the existing `cg-*` design system.
- Email templates (plain, short, link to the case).
- Seed data for the E2E database.
