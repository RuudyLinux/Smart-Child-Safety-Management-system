"""
ChildGuard face-recognition service.

Internal-only service (not exposed to the public internet) that ChildGuard.Api
calls to register a guardian's face embedding and to verify a captured face
against a stored embedding during pickup verification. See README.md.
"""
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI

from app.models.schemas import HealthResponse
from app.routes import analyze, identify, liveness, register, verify
from app.services import passive_pad
from app.services.face_engine import DETECTOR_BACKEND, MATCH_THRESHOLD, MODEL_NAME, warm_up

logging.basicConfig(level=logging.INFO)


@asynccontextmanager
async def lifespan(_: FastAPI):
    warm_up()
    passive_pad.available()  # load MiniFASNet now so the first liveness check is not slow
    yield


app = FastAPI(
    lifespan=lifespan,
    title="ChildGuard Face Service",
    description="Internal face registration/verification service (MTCNN + DeepFace/ArcFace).",
    version="1.0.0",
)

app.include_router(analyze.router)
app.include_router(identify.router)
app.include_router(liveness.router)
app.include_router(register.router)
app.include_router(verify.router)


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="ok", model=MODEL_NAME, detector=DETECTOR_BACKEND, threshold=MATCH_THRESHOLD,
                          anti_spoofing=passive_pad.available())
