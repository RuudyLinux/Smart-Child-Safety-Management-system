"""
ChildGuard face-recognition service.

Called only by ChildGuard.Api, to register a guardian's face embedding and to verify a captured face
against a stored embedding during pickup verification. It must not be open to the internet: either keep
it on a private network, or (behind a tunnel) set FACE_SERVICE_REQUIRE_KEY=true so it refuses to run
without a strong FACE_SERVICE_KEY. See README.md and app/security.py.
"""
import logging
from contextlib import asynccontextmanager
from typing import Optional

from fastapi import FastAPI, Header

from app import security
from app.models.schemas import HealthResponse
from app.routes import analyze, identify, liveness, register, verify
from app.services import passive_pad
from app.services.face_engine import DETECTOR_BACKEND, MATCH_THRESHOLD, MODEL_NAME, warm_up

logging.basicConfig(level=logging.INFO)


@asynccontextmanager
async def lifespan(_: FastAPI):
    security.startup_check()  # refuse to start "open" when a key is required
    warm_up()
    passive_pad.available()  # load MiniFASNet now so the first liveness check is not slow
    yield


# Interactive API docs list every endpoint; when the service sits behind a public tunnel they are not served.
_public = security._required()

app = FastAPI(
    lifespan=lifespan,
    title="ChildGuard Face Service",
    description="Internal face registration/verification service (MTCNN + DeepFace/ArcFace).",
    version="1.0.0",
    docs_url=None if _public else "/docs",
    redoc_url=None if _public else "/redoc",
    openapi_url=None if _public else "/openapi.json",
)

app.include_router(analyze.router)
app.include_router(identify.router)
app.include_router(liveness.router)
app.include_router(register.router)
app.include_router(verify.router)


@app.get("/health", response_model=HealthResponse, response_model_exclude_none=True)
def health(x_internal_key: Optional[str] = Header(default=None, alias="X-Internal-Key")) -> HealthResponse:
    """Liveness for anyone; model details only for a caller that holds the service key."""
    if not security.caller_is_trusted(x_internal_key):
        return HealthResponse(status="ok")
    return HealthResponse(status="ok", model=MODEL_NAME, detector=DETECTOR_BACKEND, threshold=MATCH_THRESHOLD,
                          anti_spoofing=passive_pad.available())
