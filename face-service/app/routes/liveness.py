from fastapi import APIRouter, Depends, HTTPException

from app.models.schemas import LivenessRequest, LivenessResponse
from app.security import verify_internal_key
from app.services.face_engine import FaceEngineError
from app.services.liveness import analyze_liveness

router = APIRouter()


@router.post("/liveness/analyze", response_model=LivenessResponse, dependencies=[Depends(verify_internal_key)])
def liveness(request: LivenessRequest) -> LivenessResponse:
    try:
        result = analyze_liveness(
            list(request.steps),
            [f.model_dump() for f in request.frames],
            [c.model_dump() for c in request.candidates] if request.candidates else None,
        )
    except (ValueError, FaceEngineError) as exc:
        raise HTTPException(status_code=422, detail=str(exc)) from exc
    return LivenessResponse(**result)
