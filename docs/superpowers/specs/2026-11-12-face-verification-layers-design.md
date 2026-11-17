# Guardian face verification: three layers, anti-spoofing, multiple references

Status: implemented 2026-11-12 (phases 1-7). The sections below are the plan; section 15 records what was built and where it differs. Nothing here changes the 0.40 threshold.

## 1. Intent

Pickup today is: pick guardian, send one webcam frame, ArcFace cosine >= 0.40, admin approves. A printed photo or phone screen of the guardian passes, one selfie is the only reference, image problems surface as raw errors, and the record keeps only a score.

Goal: keep the engine and admin approval exactly as they are; add three independent, separately recorded layers (image quality, identity + authorisation, liveness) and a central decision engine so the desk and the approving admin see why something passed or failed.

Non-goals: replacing ArcFace/MTCNN, changing 0.40, automatic approval, 1:N search across the whole school, new roles.
Anti-goals: a green "verified" shown before every mandatory layer passed; any path where a gallery image or a client-supplied result reaches an approvable record; claiming spoof protection is guaranteed.

## 2. Constraints (from CLAUDE.md and the owner)

- DTO changes additive only. Old endpoints (`/api/verification/face-verify`, `/approve`, `/reject`, `/api/guardians/{id}/face`, `/api/guardian/me/face`, Python `/register-face`, `/verify-face`) keep their shapes.
- Business rules only in `ChildGuard.Application`. Python returns measurements; AdminWeb is a thin proxy; JS reflects, never decides.
- Migrations are new and additive. No DROP/DELETE/TRUNCATE, no rewrite of existing rows. Existing `GuardianFaceData` rows stay as they are.
- Instants UTC; school calendar via `SchoolClock`. Secrets only in user-secrets/env.
- Pickup state, journey lock, approval transaction untouched.

## 3. Approaches considered

- A (chosen): Python does image work (quality, detection, embedding/matching, liveness signals); C# owns the decision engine, sessions, authorisation and storage.
- B: decision engine in Python. Rejected: splits business rules across two codebases, not testable with the existing fakes.
- C: quality checks in browser JS. Rejected: untrusted; a tampered client could skip the gallery/liveness rules. The browser may show hints but the server re-checks everything.

Liveness: randomised head-turn challenge + MiniFASNet passive anti-spoofing + frame-sequence consistency. Blink detection deferred (needs MediaPipe).

## 4. Layer results (kept independent)

| Layer | Values | Computed by |
|---|---|---|
| Image quality | PASS / FAIL | Python measures, C# decides |
| Face detection | PASS / FAIL | Python measures, C# decides |
| Identity | MATCH / NO_MATCH / INCONCLUSIVE | Python scores, C# decides |
| Authorisation | AUTHORISED / UNAUTHORISED / NOT_EVALUATED | C# only |
| Liveness | PASS / FAIL / NOT_PERFORMED | Python signals, C# decides |
| Admin approval | PENDING / APPROVED / REJECTED | existing `GetState` (+ Expired, NotMatched kept) |

Decision engine rules (`VerificationDecisionEngine`, pure, table-tested):
1. Any of quality, detection FAIL: outcome `RetryCapture`, nothing approvable.
2. Identity NO_MATCH: `NotRecognised`. INCONCLUSIVE: `NeedsReview` (retry or admin reject; never approvable).
3. Matched guardian linked to the student but not `IsAuthorizedForPickup`: UNAUTHORISED, blocked. Candidates are only guardians linked to the selected student, so a guardian of another child is simply NO_MATCH (cross-student verification impossible) and no unrelated guardian is ever loaded or named.
4. Liveness NOT_PERFORMED (gallery): outcome `LiveVerificationRequired`, never approvable. FAIL: `PossibleSpoof`, blocked.
5. Only quality PASS + detection PASS + MATCH + AUTHORISED + liveness PASS creates a `GuardianVerification` (state Pending). An admin still approves or rejects. High score never short-circuits any rule.

Identity detail: score = max cosine over the guardian's active references (legacy row + new rows). `MATCH` = score >= Python's `MATCH_THRESHOLD` (0.40, unchanged). `INCONCLUSIVE` only when two different guardians both reach the threshold within `Verification:AmbiguityMargin`. Default 0.0 (off) until the owner approves a value; it can never turn a NO_MATCH into MATCH. Confidence text for admins is derived from score vs threshold and is informational.

## 5. Workflow

1. Desk identifies the student (RFID, existing). `POST /api/verification/sessions {studentId}` opens a session (admin-bound, 10-minute expiry) and returns the student's authorised guardians (id, name, relationship, face registered, reference count). No embeddings.
2. Staff chooses webcam or gallery.
3. Gallery: `POST /sessions/{id}/identify {source:"Gallery", imageBase64}` runs quality, detection, identity, authorisation. Result is stored on the session only (no `GuardianVerification` row), liveness NOT_PERFORMED, next step `LiveVerificationRequired`.
4. Webcam (and the live step after a gallery match): `POST /sessions/{id}/challenge` returns a random 2-step turn sequence (e.g. left, right) and a single-use token valid 30 s (stored as SHA-256). Browser records 8-16 frames; `POST /sessions/{id}/live-verify {challengeToken, frames[]}`.
5. Server forwards frames to Python once: quality on the neutral frame, identity from that same frame, yaw series, passive anti-spoof scores, embedding continuity across frames. C# runs the decision engine. The live face must match the same guardian as the gallery step if one was done, so a gallery photo of the guardian plus a different live person fails.
6. Token is consumed whatever the outcome (replay refused, retry needs a new challenge). After `MaxLiveAttempts` (5) failures the session closes.
7. Pass: pending `GuardianVerification` created with all layer fields; response carries verificationId/expiresAt. Admin approves or rejects through the existing endpoints.
8. Approval gate in `ApproveAsync`: if `Verification:RequireLiveness` is true, a verification whose `LivenessResult` is not PASS (including legacy rows with none) is refused. Reject stays allowed. See decisions below on the default.

## 6. Python service (additive endpoints, `X-Internal-Key` as today)

`/analyze-image` never returns 422 for a bad file: format, size and resolution problems are normal 200 results with `BAD_FORMAT` / `TOO_LARGE` / `LOW_RESOLUTION` issue codes. `/identify` and `/liveness/analyze` return 422 with a plain `detail` for undecodable input.

`POST /analyze-image` `{image_base64, purpose:"registration"|"verification"}` returns:
```
{ file:{format,bytes,width,height}, face_count, faces:[{x,y,w,h,confidence}],
  metrics:{sharpness_laplacian, brightness_mean, clipped_dark_frac, clipped_bright_frac,
           face_px, yaw_deg_est, pitch_est, eye_region_contrast, obstruction_suspect},
  issues:[ "NO_FACE" | "MULTIPLE_FACES" | "BLURRY" | "TOO_DARK" | "TOO_BRIGHT" | "LOW_CONTRAST" |
           "FACE_TOO_SMALL" | "EXTREME_ANGLE" | "FACE_OBSTRUCTED" | "BAD_FORMAT" | "TOO_LARGE" ],
  messages:[...], limits_used:{...} }
```
Validation: magic bytes (JPEG/PNG/WebP, not extension), <= 2 MB, <= 4096 px per side, decode guarded. Thresholds in env vars (`FACE_MIN_SHARPNESS`, etc.), defaults chosen on evaluation data and recorded in the report. Obstruction and angle are heuristics (detector confidence, landmark geometry, eye-region darkness/contrast); documented as such.

`POST /identify` `{image_base64, candidates:[{guardian_id, references:[{reference_id, embedding_json}]}]}` returns per guardian `{guardian_id, best_score, best_reference_id, scores:[...]}`, `threshold`, `model`. Embedding of the largest face, same engine as `/verify-face`; threshold from the existing env var.

`POST /liveness/analyze` `{steps:["turn_left","turn_right"], frames:[{image_base64, t_ms}]}` returns:
```
{ frames_used, face_found_frac, neutral_frame_index, yaw_series:[..],
  steps_completed:[true,true], continuity_min_cosine, same_person:true,
  passive:{ frames_scored, real_scores:[..], mean, min, available:true },
  duplicate_frame_frac, timing:{span_ms, min_gap_ms}, embedding_json_of_neutral_frame? (no) }
```
Yaw = horizontal nose offset from the eye midpoint over inter-eye distance, from the 5 MTCNN keypoints returned by the `mtcnn` package directly (DeepFace's wrapper drops nose and mouth; the package is already installed). Passive scoring uses `DeepFace.extract_faces(..., anti_spoofing=True)` on at most 5 frames. If the PAD model cannot load, `passive.available=false` and C# returns liveness FAIL/`LIVENESS_UNAVAILABLE`, never PASS (fail closed).

Existing `/register-face`, `/verify-face`, `/health` unchanged (`/health` gains optional fields).

## 7. Database (all additive, three migrations, `has-pending-model-changes` checked each time)

M1 `AddGuardianFaceReferences`
`GuardianFaceReferences(Id, GuardianId FK RESTRICT, EmbeddingProtected LONGTEXT, KeyId varchar(32) null, Model varchar(50), Label varchar(60) null, QualityJson varchar(1000), IsActive, CreatedAt, CreatedByRole, CreatedById)`, index `(GuardianId, IsActive)`. No uniqueness on guardian. Cap `Biometrics:MaxReferencesPerGuardian` = 5 (the legacy row counts as one). Near-duplicate (cosine >= 0.97 against an existing active reference, checked through `/identify`) is not stored. "Remove" sets `IsActive=false`; hard purge is a separate approved step.
Compatibility: reading references = legacy `GuardianFaceData` row (plaintext, untouched) + active new rows (AES-256-GCM when `Biometrics:EncryptionKey` is set, KeyId recorded). A backfill that encrypts the legacy row is a data rewrite and is not done without approval. The old single-photo endpoints keep replacing only the legacy row.

M2 `AddVerificationSessionsAndLayerResults`
`VerificationSessions(Id, StudentId FK RESTRICT, CreatedByAdminId, CreatedAt, ExpiresAt, State{Open,Completed,Expired,Closed}, GalleryGuardianId null, GalleryScore null, ChallengeTokenHash varchar(64) null, ChallengeSteps varchar(40) null, ChallengeIssuedAt null, ChallengeExpiresAt null, ChallengeConsumedAt null, FailedLiveAttempts, NeutralFrameHash varchar(64) null UNIQUE)`.
`GuardianVerifications` + nullable columns: `CaptureSource, QualityResult, FaceDetectionResult, IdentityResult, AuthorisationResult, LivenessResult, LivenessScore, BestReferenceId, SessionId, LayerNotes varchar(500)`. Legacy rows keep nulls and render as "legacy, no liveness".
`NeutralFrameHash` unique index refuses byte-identical replay of an earlier capture.

M3 `AddFaceAuditLog`
`FaceAuditLogs(Id, OccurredAt UTC, ActorRole, ActorId, Layer{Quality,Detection,Identity,Authorisation,Liveness,Decision,Reference,Access}, Action, Outcome, StudentId null, GuardianId null, VerificationId null, SessionId null, Detail varchar(1000))`, indexes on `OccurredAt`, `GuardianId`. Plain int columns (append-only, no FKs). Detail holds codes and scores, never images, embeddings or tokens.

## 8. C# services

- `Application/Verification/Layers`: enums, `LayerResults`, `VerificationDecisionEngine`, `FaceQualityPolicy` (maps Python metrics + issues to PASS/FAIL), `IdentityPolicy`.
- `VerificationSessionService` (create, challenge, identify, live-verify, expiry, single use, attempts, binding to admin + student + session). Clock is `TimeProvider`/`SchoolClock`; tests advance it.
- `GuardianFaceReferenceService` (add/list/remove, validate each photo independently, dedup, cap, encrypt).
- `IEmbeddingProtector` (Infrastructure, AES-GCM, key from config, rotation via KeyId); `IFaceAuditLog`.
- `IFaceServiceClient` extended with `AnalyzeImageAsync`, `IdentifyAsync`, `AnalyzeLivenessAsync` (new methods; existing two untouched; test fakes updated).
- `VerificationService.ApproveAsync` gains the liveness gate only. Everything else in it is unchanged.
- Options: `Biometrics:*` (EncryptionKey, MaxReferencesPerGuardian, AuditRetentionDays=0, SessionRetentionDays=0, RetainVerificationImages=false and not implementable), `Verification:RequireLiveness`, `AmbiguityMargin`, `MaxLiveAttempts`, `ChallengeSeconds`=30, `SessionMinutes`=10, `MinFrames`/`MaxFrames`.
- New API endpoints (all `AdminOnly`, rate limited per admin, request size limited):
  - `POST /api/verification/analyze-image`
  - `POST /api/verification/sessions`, `POST /sessions/{id}/identify`, `POST /sessions/{id}/challenge`, `POST /sessions/{id}/live-verify`
  - `GET/POST/DELETE /api/guardians/{id}/face-references`
  - `GET/POST/DELETE /api/guardian/me/face-references` and `POST /api/guardian/me/face-references/analyze` (guardian app; own data only)
- Responses never include embeddings or other guardians' data.

## 9. Security architecture

- Never trust the client: quality, identity, liveness are computed server-side from raw bytes; request fields only carry bytes, source label and the challenge token. Source label is also checked: a "Webcam" source needs a valid challenge/frames; the gallery endpoint cannot write a liveness result.
- Challenge token: 256-bit random, stored hashed, single use, 30 s, bound to session (and so to admin + student), consumed even on failure. Sessions expire after 10 minutes; cleanup is lazy plus on create.
- Rate limits: new `FaceAnalyze` (60/min) and `LiveVerify` (20/min) policies partitioned per admin; image 2.5 MB cap, live burst 3 MB, 16 frames max.
- Replay: single-use token, random 2-step sequence (4 variants, a naive replay passes the challenge about 1 in 4, so it is a speed bump, not the main defence), `NeutralFrameHash` uniqueness, duplicate-frame fraction, minimum capture span, passive PAD.
- Encryption: AES-256-GCM for new embeddings, key outside the repo, startup warning when missing in production; TLS between API and face service documented in `PRODUCTION_DEPLOYMENT.md`.
- Privacy: no verification photo or frame is written to disk or DB (held in memory for the request); `/analyze-image` and `/liveness/analyze` log scores only. Retention/deletion settings exist but default to keep-nothing-new / delete-nothing; enabling purge of audit rows or sessions needs explicit approval and starts as a dry run.
- Audit: each layer writes its own `FaceAuditLogs` row; access denials logged.
- Access: reception staff are `Admin` accounts (no other role exists). Desk endpoints only return the selected student's guardians; embeddings are never serialised. Finer roles are listed as a future recommendation.

## 10. UI

AdminWeb (thin proxy actions `StartSession`, `AnalyzeImage`, `Identify`, `Challenge`, `LiveVerify` in `VerificationController`):
- Shared `wwwroot/js/face-capture.js`: webcam + gallery picker + drag and drop, preview, replace, remove, upload progress (XHR), per-check checklist, retry. Used by the desk and the guardian Face ID tab.
- Desk (`Views/Verification/Index.cshtml`, `verification.js`): replaces the single "Capture & verify" with the 10-step flow. Checklist rows: quality, face detected, identity, authorisation, liveness, each with its own message. Green "verified" only when the server returns all mandatory layers passed. Head-turn prompt with countdown for the live step. Approve/Reject buttons as today, enabled only for a pending record; panel shows score vs threshold, which reference matched (label), liveness result.
- Guardian page (`face-enrol.js`, Photo & Face ID tab): list of references (label, date, quality), add via webcam or gallery, remove, per-photo feedback.
- Verification log (`History.cshtml`): extra columns for layer results (legacy rows show "legacy").

Flutter (`face_registration_screen.dart`, `guardian_api_service.dart`, `models.dart`): multi-photo registration from camera or gallery (`ImageSource.gallery`), per-photo analysis feedback before saving, list and remove. Existing single-photo call kept.

## 11. Evaluation harness (`face-service/eval/`, stdlib + existing deps only)

- `identity_eval.py`: input is a folder `root/<person>/<image>` plus optional `meta.csv` (`file,camera,lighting,session`). Builds genuine and impostor pairs, embeds once, writes `scores.csv`, then FAR/FRR/genuine-accept/impostor-accept over a threshold sweep, values at 0.40, EER, per-lighting and per-camera tables, and the multi-reference variant (enrol k, probe the rest, max-score). Output `docs/face-evaluation/identity-<date>.md/json`.
- Public baseline: LFW (funneled) downloaded to a git-ignored dir; this measures the model, not the school's cameras. If a download is blocked I report that and what was run instead.
- School set: drop consented photos in `eval/datasets/school/` with `meta.csv`; same script, same report with a "school" section. Collection protocol (consent text, conditions to cover, retention, deletion after evaluation) in `eval/README.md`. Nothing from that set is committed.
- `pad_eval.py`: folders `live/`, `print/`, `screen/` (and optional `video_replay/`) run through `/liveness/analyze` logic; reports APCER, BPCER, ACER per attack type. Public PAD corpora (CASIA-FASD, Replay-Attack, OULU) require licence agreements, so they are not auto-downloaded. If no attack samples exist the report says "not measured" instead of inventing numbers; a capture protocol for printed/screen attacks is documented.
- Threshold changes are only ever written as a recommendation in the report.

## 12. Testing

- Python: `unittest` (no new dependency) for validation, quality metrics (synthetic blur/dark/bright/small images, mocked detector), yaw math from synthetic keypoints, passive-unavailable fails closed.
- C# unit: decision engine truth table (every layer combination), session service (expiry, single use, binding, attempts, replay hash), reference service (cap, dedup, encryption round-trip, no embeddings in DTOs), approval gate.
- Integration (real API, throwaway MySQL, extended fake face client): gallery never yields an approvable record; token reuse, expired token, other admin's session, wrong student, cross-student guardian, oversize image, rate limit; old endpoints byte-compatible; migrations apply on top of the existing model.
- Regression after every phase: `dotnet build`, both .NET test projects, `flutter analyze && flutter test` when Flutter changes, Python tests. Baseline numbers recorded before Phase 2.
- Live checks with the real engine on LFW images and on the desk page in a browser; what cannot be verified (PAD against real attacks without samples, multiple physical cameras) is reported as not verified.

## 13. Phases

1. Audit + this plan (done).
2. Python `/analyze-image`, C# client + `FaceQualityPolicy` + `POST /analyze-image`, shared `face-capture.js` (gallery + webcam, preview, quality feedback) wired into the guardian Face ID tab and the desk, audit table (M3 moved here so every layer logs from the start).
3. M1 references, `GuardianFaceReferenceService`, `/identify`, identity policy, admin + guardian-app multi-photo UI.
4. Python `/liveness/analyze` (adds `torch` CPU, see decisions), M2 sessions, challenge/live-verify.
5. Decision engine wired end to end, authorisation rules, approval gate, history DTO fields.
6. Desk UI flow, review panel, log columns, Flutter.
7. Security tests, LFW evaluation, PAD evaluation or documented gap, regression run, final report.

## 14. Decisions I made that you may want to change

1. `torch` (CPU wheel, pinned) is required: DeepFace's MiniFASNet is a PyTorch model and torch is not installed. It is the only new dependency, needed for the passive anti-spoofing you asked for. The two MiniFASNet weight files download from GitHub on first use (internet once, then offline). If torch cannot be installed on this Python 3.8 environment, liveness stays fail-closed and I report it.
2. `Verification:RequireLiveness` ships `false` until Phase 6 so the current desk keeps working during incremental phases, then the default becomes `true`. While it is false the old `/face-verify` path can still produce approvable rows without liveness, which is exactly the bypass this feature closes.
3. `AmbiguityMargin` default 0.0 (inconclusive state off) pending your number.
4. Retention purge for audit rows and sessions is implemented but disabled (0 = keep) because deleting data needs your approval.
5. Head-turn yaw uses the 5 MTCNN keypoints through the `mtcnn` package, not DeepFace's wrapper (which exposes only the eyes).
6. Obstruction and extreme-angle detection are heuristics, not a trained occlusion model. Found in Phase 2 testing: the lower-face colour test for masks flagged a plain face with heavy eye make-up and lipstick as strongly obstructed, so it only warns; only the sunglasses test (very dark, flat eye regions) blocks.
8. Phase 3 changed the storage design from the plan: the guardian's first face stays in `GuardianFaceData` (so every existing reader and the `FaceRegistered` flag keep working) and only extra photos go to `GuardianFaceReferences`. Encryption is carried in the stored value itself (`enc:v1:<keyId>:<payload>`, bound to the guardian id) instead of a `KeyId` column, so old plain rows and new encrypted rows coexist without rewriting any data. Python has `/register-reference` (embedding + duplicate check in one call) instead of reusing `/identify`.
9. New rule for the new feature (flagged for your approval, uses the existing 0.40 line unchanged): an extra reference must score at or above the match threshold against a face already on file, otherwise it is refused. Without it, anyone able to add a photo could register a different person as the guardian. A changed appearance goes through "replace primary face". On the sample photos, same-person pairs scored 0.58-0.87 and different people -0.02-0.22.
7. `/analyze-image` hosts the guardian Face ID registration UI (gallery or camera) in Phase 2; the pickup desk adopts the same widget in the unified flow (Phases 5-6).


## 15. As built (2026-11-12)

All seven phases are implemented and tested. Differences from the plan above:

- **Storage:** the first face stays in `GuardianFaceData`; extra photos are in `GuardianFaceReferences`. Encryption is carried in the stored value (`enc:v1:<keyId>:...`, bound to the guardian id) rather than a `KeyId` column. Migrations: `AddFaceAuditLog`, `AddGuardianFaceReferences`, `AddVerificationSessionsAndLayerResults` (all additive; one table or ten nullable columns each).
- **Python:** `/analyze-image`, `/identify`, `/register-reference`, `/liveness/analyze` added; `/register-face`, `/verify-face`, `/health` unchanged (`/health` gained `anti_spoofing`). `/liveness/analyze` also returns the frontal frame's quality and its identification, so one call ties identity to the live frames.
- **Torch:** `torch==2.4.1` (CPU) installed and pinned in `requirements.txt`; MiniFASNet weights (2 x 1.9 MB) download on first start.
- **Approval gate:** `Verification:RequireLiveness` now defaults to `true` (flipped in phase 6 as planned). A layered record whose liveness is not `Pass`, or whose source is a gallery image, is never approvable, whatever the switch says.
- **Failed attempts leave records:** a recognised guardian whose liveness failed, or who is not authorised, produces a non-approvable `GuardianVerification` (Result `NoMatch`, layer fields set) so the log shows spoofing attempts; unrecognised faces and unusable images are audit rows only.
- **Challenge:** two random head-turn steps (four possible sequences), 256-bit token stored as SHA-256, 30 s, one submission, bound to session and admin, concurrency token on the session so two simultaneous submissions cannot both succeed. The neutral frame's SHA-256 is unique across sessions (replay).
- **Not built:** blink detection (deferred as agreed); retention purge jobs (no purge exists, so nothing is deleted); legacy-embedding encryption backfill (data rewrite, needs approval); a separate reception role.
- **Evaluation:** see `docs/face-evaluation/` and `face-service/eval/README.md`. The 0.40 threshold, the ambiguity margin and the liveness thresholds are unchanged or off by default; recommendations are in the reports.
