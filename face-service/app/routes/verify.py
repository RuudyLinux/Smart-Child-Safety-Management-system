from fastapi import APIRouter, Depends, HTTPException

from app.models.schemas import VerifyFaceRequest, VerifyFaceResponse
from app.security import verify_internal_key
from app.services.face_engine import MATCH_THRESHOLD, FaceEngineError, verify_face

router = APIRouter()


@router.post("/verify-face", response_model=VerifyFaceResponse, dependencies=[Depends(verify_internal_key)])
def verify(request: VerifyFaceRequest) -> VerifyFaceResponse:
    try:
        match, score = verify_face(request.stored_embedding_json, request.image_base64)
    except FaceEngineError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return VerifyFaceResponse(match=match, score=score, threshold=MATCH_THRESHOLD)
