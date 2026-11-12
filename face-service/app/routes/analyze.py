from fastapi import APIRouter, Depends

from app.models.schemas import AnalyzeImageRequest, AnalyzeImageResponse
from app.security import verify_internal_key
from app.services.image_quality import analyze

router = APIRouter()


@router.post("/analyze-image", response_model=AnalyzeImageResponse, dependencies=[Depends(verify_internal_key)])
def analyze_image(request: AnalyzeImageRequest) -> AnalyzeImageResponse:
    return AnalyzeImageResponse(**analyze(request.image_base64, request.purpose))
