# ChildGuard Face Service

Internal Python service for guardian face registration and pickup face verification.
Called only by `ChildGuard.Api` — never exposed to the public internet.

Stack: FastAPI, OpenCV, DeepFace (MTCNN face detection + alignment, ArcFace embeddings).
Stores/returns embeddings only, never raw images — the caller persists the embedding in MySQL.

How a check works: find the face (the largest one in the frame), level it using the eye
positions, turn it into an ArcFace embedding, and compare it with the stored embedding
by cosine similarity (1.0 = identical). At or above the threshold is a match; an admin
still approves every matched pickup.

Registration is stricter than verification: exactly one clear face, at least
`FACE_MIN_SIZE_PX` pixels, confidently detected. Refusals come back as HTTP 422 with a
plain-language `detail` the desk shows as is (no face found, face too small, more than
one face, unreadable image).

## Endpoints

- `POST /register-face` — `{ "image_base64": "..." }` → `{ "embedding_json": "...", "model": "ArcFace" }`
- `POST /verify-face` — `{ "stored_embedding_json": "...", "image_base64": "..." }` → `{ "match": true, "score": 0.83, "threshold": 0.4 }`
- `GET /health` — `{ "status": "ok", "model": "ArcFace", "detector": "mtcnn", "threshold": 0.4 }`
- `POST /identify` — `{ "image_base64": "...", "candidates": [ { "guardian_id": 1, "references": [ { "reference_id": 0, "embedding_json": "..." } ] } ] }` → `{ "threshold": 0.4, "model": "ArcFace", "results": [ { "guardian_id": 1, "best_score": 0.86, "best_reference_id": 0, "scores": [...], "unreadable_reference_ids": [] } ] }`. Scores one capture against every reference of every candidate (at most 20 candidates, 10 references each). Measures only; the caller decides. An unreadable reference is reported, not fatal.
- `POST /register-reference` — `{ "image_base64": "...", "existing": [ { "reference_id": 0, "embedding_json": "..." } ] }` → `{ "embedding_json": "...", "model": "ArcFace", "duplicate_of": null, "best_existing_score": 0.7, "threshold": 0.4 }`. Same strict one-clear-face rules as `/register-face`, plus similarity to the existing references: `duplicate_of` is set when one is at least `FACE_DUPLICATE_SIMILARITY` (default `0.95`, not calibrated) similar, so the same photo is not stored twice.
- `POST /liveness/analyze` — `{ "steps": ["turn_left","turn_right"], "frames": [ { "image_base64": "...", "t_ms": 0 } ], "candidates": [...] }` (6 to 16 frames) → signals for one webcam burst: `challenge_completed` with per-step results, the `yaw_series`, `passive` (MiniFASNet P(real) on up to 5 frames: `available`, `real_scores`, `real_frac`, `mean`), `continuity` (the same face in the frontal and turned frames), duplicate-frame fraction, timing, the frontal frame's `neutral_quality` (same shape as `/analyze-image`) and, when `candidates` are sent, `identification` for the frontal frame (same shape as `/identify`), plus `issues` (e.g. `FACE_LOST`, `WRONG_DIRECTION`, `NO_NEUTRAL_FRAME`, `DUPLICATE_FRAMES`, `BAD_TIMING`, `PASSIVE_UNAVAILABLE`, `FACE_CHANGED`). Signals only; ChildGuard.Api decides. "Left" means the person's own left: the nose moves toward the right of the raw, unmirrored frame.
- `POST /analyze-image` — `{ "image_base64": "...", "purpose": "registration" | "verification" }` → measurements for Layer 1 (is this image fit for recognition?): `passed`, `file` (format, bytes, size), `face_count`, `faces`, `metrics` (sharpness, brightness, contrast, face size, estimated yaw/pitch/roll, obstruction signals), `issues` (blocking codes such as `NO_FACE`, `MULTIPLE_FACES`, `BLURRY`, `TOO_DARK`, `TOO_BRIGHT`, `FACE_TOO_SMALL`, `FACE_CUT_OFF`, `EXTREME_ANGLE`, `FACE_OBSTRUCTED`, `BAD_FORMAT`, `TOO_LARGE`, `LOW_RESOLUTION`), `warnings` (non-blocking) and plain-language `messages`. A bad file is a normal 200 result with `BAD_FORMAT`/`TOO_LARGE`, never a 500. It only measures; ChildGuard.Api decides what blocks.

## Setup

```
python -m venv .venv
.venv\Scripts\activate          # Windows
pip install -r requirements.txt
```

First run downloads the ArcFace model weights (DeepFace caches them under
`~/.deepface/weights`) — needs internet access once, then works offline.

## Run

```
uvicorn app.main:app --host 0.0.0.0 --port 8001
```

Or use the repo-root `start.bat` / `stop.bat`, which manage this alongside the API and Admin Web.

## Configuration

`ChildGuard.Api` calls this service at the URL configured in its
`FaceService:BaseUrl` setting (default `http://localhost:8001`). It holds no
database connection and no credentials.

### Matching

| Variable | Default | Meaning |
|---|---|---|
| `FACE_MATCH_THRESHOLD` | `0.40` | Minimum cosine **similarity** for a match. DeepFace's published ArcFace value is a cosine *distance* of 0.68 (= similarity 0.32); 0.40 is the school's chosen, slightly stricter margin (2026-11-07). Raising it means more genuine guardians fail; lowering it means more look-alikes pass. |
| `FACE_DETECTOR` | `mtcnn` | DeepFace detector backend. MTCNN works offline (weights ship with the pip package) and aligns faces. `retinaface` is more accurate but downloads ~120 MB of weights on first use. |
| `FACE_MIN_SIZE_PX` | `80` | Smallest face side accepted at registration. |

### Liveness (`/liveness/analyze`)

Needs PyTorch (`torch==2.4.1`, CPU) for the passive anti-spoofing model (MiniFASNet, two 1.9 MB weight files that DeepFace
downloads to `~/.deepface/weights` on first use, then works offline). If PyTorch or the weights are missing the service still
runs but `/health` reports `anti_spoofing: false` and every liveness analysis reports `PASSIVE_UNAVAILABLE`: liveness can never pass
without it. Head yaw comes from the five MTCNN keypoints (DeepFace's own wrapper drops the nose, so the `mtcnn` package is read directly).

| Variable | Default | Meaning |
|---|---|---|
| `LIVENESS_TURN_MIN_RATIO` | `0.20` | Nose offset (in eye distances) a head turn must reach, about 17 degrees by the uncalibrated yaw estimate. |
| `LIVENESS_NEUTRAL_MAX_RATIO` / `LIVENESS_RETURN_MAX_RATIO` | `0.12` / `0.12` | How frontal the starting frame and the return to centre between turns must be. |
| `LIVENESS_MIN_FRAMES` / `LIVENESS_MAX_FRAMES` | `6` / `16` | Frames accepted per burst. |
| `LIVENESS_MIN_SPAN_MS` / `LIVENESS_MAX_SPAN_MS` | `1500` / `15000` | Plausible duration of a burst. |
| `LIVENESS_MIN_FACE_FOUND_FRAC` | `0.70` | Share of frames in which the face must be found. |
| `LIVENESS_CONTINUITY_MIN` | `0.25` | Minimum ArcFace similarity between the frontal and the turned frames. **Not calibrated on real head turns.** |
| `LIVENESS_MAX_DUPLICATE_FRAC` | `0.30` | Share of byte-identical frames that marks a replay. |
| `LIVENESS_PAD_FRAMES` | `5` | Frames scored by the passive model. |

These defaults have not been calibrated on real webcams. The decision thresholds on the passive scores live in the API
(`FaceVerification:PassiveMinRealFraction`, `PassiveMinMeanReal`), not here.

### Image checks (`/analyze-image`)

| Variable | Default | Meaning |
|---|---|---|
| `FACE_MAX_IMAGE_BYTES` | `2097152` | Largest accepted image (decoded bytes). Checked from the base64 length before decoding. |
| `FACE_MAX_SIDE_PX` | `4096` | Largest accepted width/height, read from the file header (no full decode of an oversized image). |
| `FACE_MIN_IMAGE_SIDE_PX` | `160` | Smallest accepted width/height. |
| `FACE_MIN_DETECTION_CONFIDENCE` | `0.90` | MTCNN confidence below which a detection is ignored. |
| `FACE_SECOND_FACE_AREA_RATIO` | `0.35` | A second face at least this fraction of the main face's area is `MULTIPLE_FACES`; smaller background faces are ignored. |
| `FACE_MIN_SHARPNESS` | `45` | Laplacian variance of the face crop resized to 128 px. **Not calibrated on real webcams yet**; tune from the evaluation report. |
| `FACE_MIN_BRIGHTNESS` / `FACE_MAX_BRIGHTNESS` | `55` / `200` | Mean grey level of the central face area. |
| `FACE_MAX_DARK_CLIPPED_FRAC` / `FACE_MAX_BRIGHT_CLIPPED_FRAC` | `0.30` / `0.20` | Share of crushed-black / blown-out pixels in the face crop. |
| `FACE_MIN_CONTRAST` | `22` | Grey-level spread of the face; low is a warning (a blocker for registration photos). |
| `FACE_MAX_YAW_DEG` / `FACE_MAX_ROLL_DEG` | `30` / `30` | Estimated head turn / tilt. Yaw is an uncalibrated estimate from the nose position between the eyes. |

Limits to know: blur, exposure, angle and obstruction are single-frame heuristics. Obstruction **blocks** only on very dark, flat eye regions (sunglasses); a lower-face colour test for masks only **warns**, because on a plain face with heavy make-up it gave a false "strongly obstructed". A hand over the mouth or subtle occlusion is not detected.

Every verification logs its similarity and the threshold (never the image), so the
threshold can be tuned from real use. Changing the detector does not invalidate stored
faces; they are ArcFace embeddings either way.

The models load at startup (a few seconds), so the first pickup check is not slow.

### Internal auth (optional, recommended for anything beyond localhost dev)

Set the `FACE_SERVICE_KEY` environment variable to require every request to
carry a matching `X-Internal-Key` header. Leave it unset for local dev — the
check is skipped and the service behaves as before. When set here, also set
the same value as `FaceService:ApiKey` in `ChildGuard.Api`'s configuration so
its calls keep working.
