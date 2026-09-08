from fastapi import APIRouter, HTTPException

from app.models.schemas import RegisterFaceRequest, RegisterFaceResponse
from app.services.face_engine import FaceEngineError, register_face

router = APIRouter()


@router.post("/register-face", response_model=RegisterFaceResponse)
def register(request: RegisterFaceRequest) -> RegisterFaceResponse:
    try:
        embedding_json, model = register_face(request.image_base64)
    except FaceEngineError as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return RegisterFaceResponse(embedding_json=embedding_json, model=model)
