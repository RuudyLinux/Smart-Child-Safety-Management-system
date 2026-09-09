# ChildGuard Face Service

Internal Python service for guardian face registration and pickup face verification.
Called only by `ChildGuard.Api` — never exposed to the public internet.

Stack: FastAPI, OpenCV, DeepFace (ArcFace model). Stores/returns embeddings only,
never raw images — the caller persists the embedding in MySQL.

## Endpoints

- `POST /register-face` — `{ "image_base64": "..." }` → `{ "embedding_json": "...", "model": "ArcFace" }`
- `POST /verify-face` — `{ "stored_embedding_json": "...", "image_base64": "..." }` → `{ "match": true, "score": 0.83 }`
- `GET /health`

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

### Internal auth (optional, recommended for anything beyond localhost dev)

Set the `FACE_SERVICE_KEY` environment variable to require every request to
carry a matching `X-Internal-Key` header. Leave it unset for local dev — the
check is skipped and the service behaves as before. When set here, also set
the same value as `FaceService:ApiKey` in `ChildGuard.Api`'s configuration so
its calls keep working.
