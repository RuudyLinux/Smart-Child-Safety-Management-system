from fastapi import APIRouter, HTTPException

from app.models.schemas import VerifyFaceRequest, VerifyFaceResponse
from app.services.face_engine import FaceEngineError, verify_face

router = APIRouter()


@router.post("/verify-face", response_model=VerifyFaceResponse)
def verify(request: VerifyFaceRequest) -> VerifyFaceResponse:
    try:
        match, score = verify_face(request.stored_embedding_json, request.image_base64)
    except FaceEngineError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return VerifyFaceResponse(match=match, score=score)
