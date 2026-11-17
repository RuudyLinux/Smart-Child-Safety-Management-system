from fastapi import APIRouter, Depends, HTTPException

from app.models.schemas import (
    IdentifyRequest, IdentifyResponse, RegisterReferenceRequest, RegisterReferenceResponse,
)
from app.security import verify_internal_key
from app.services.face_engine import FaceEngineError, identify_face, register_reference

router = APIRouter()


@router.post("/identify", response_model=IdentifyResponse, dependencies=[Depends(verify_internal_key)])
def identify(request: IdentifyRequest) -> IdentifyResponse:
    try:
        return IdentifyResponse(**identify_face(request.image_base64, [c.model_dump() for c in request.candidates]))
    except FaceEngineError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc


@router.post("/register-reference", response_model=RegisterReferenceResponse, dependencies=[Depends(verify_internal_key)])
def register_ref(request: RegisterReferenceRequest) -> RegisterReferenceResponse:
    try:
        return RegisterReferenceResponse(**register_reference(request.image_base64, [e.model_dump() for e in request.existing]))
    except FaceEngineError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
