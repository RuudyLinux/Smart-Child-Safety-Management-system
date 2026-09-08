"""
ChildGuard face-recognition service.

Internal-only service (not exposed to the public internet) that ChildGuard.Api
calls to register a guardian's face embedding and to verify a captured face
against a stored embedding during pickup verification. See README.md.
"""
import logging

from fastapi import FastAPI

from app.models.schemas import HealthResponse
from app.routes import register, verify
from app.services.face_engine import MODEL_NAME

logging.basicConfig(level=logging.INFO)

app = FastAPI(
    title="ChildGuard Face Service",
    description="Internal face registration/verification service (OpenCV + DeepFace/ArcFace).",
    version="1.0.0",
)

app.include_router(register.router)
app.include_router(verify.router)


@app.get("/health", response_model=HealthResponse)
def health() -> HealthResponse:
    return HealthResponse(status="ok", model=MODEL_NAME)
