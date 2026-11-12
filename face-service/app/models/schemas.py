"""Request/response schemas for the face-recognition service."""
from typing import Any, Dict, List, Literal, Optional

from pydantic import BaseModel, Field


class RegisterFaceRequest(BaseModel):
    image_base64: str = Field(..., description="Base64-encoded JPEG/PNG image of the guardian's face.")


class RegisterFaceResponse(BaseModel):
    embedding_json: str = Field(..., description="JSON-encoded embedding vector — store this, not the image.")
    model: str = Field(..., description="Model used to produce the embedding, e.g. ArcFace.")


class VerifyFaceRequest(BaseModel):
    stored_embedding_json: str = Field(..., description="Previously registered embedding (JSON array), from ChildGuard's database.")
    image_base64: str = Field(..., description="Base64-encoded image captured at the pickup verification step.")


class VerifyFaceResponse(BaseModel):
    match: bool
    score: float = Field(..., description="Cosine similarity between the two embeddings, 0.0-1.0.")
    threshold: float = Field(..., description="Similarity at or above which the faces are treated as the same person.")


class ReferenceEmbedding(BaseModel):
    reference_id: int
    embedding_json: str


class IdentifyCandidate(BaseModel):
    guardian_id: int
    references: List[ReferenceEmbedding] = Field(..., max_length=10)


class IdentifyRequest(BaseModel):
    image_base64: str
    candidates: List[IdentifyCandidate] = Field(..., min_length=1, max_length=20)


class ReferenceScore(BaseModel):
    reference_id: int
    score: float


class GuardianIdentification(BaseModel):
    guardian_id: int
    best_score: Optional[float] = None
    best_reference_id: Optional[int] = None
    scores: List[ReferenceScore]
    unreadable_reference_ids: List[int]


class IdentifyResponse(BaseModel):
    threshold: float = Field(..., description="Similarity at or above which two faces are treated as the same person.")
    model: str
    results: List[GuardianIdentification]


class RegisterReferenceRequest(BaseModel):
    image_base64: str
    existing: List[ReferenceEmbedding] = Field(default_factory=list, max_length=10)


class RegisterReferenceResponse(BaseModel):
    embedding_json: str
    model: str
    duplicate_of: Optional[int] = Field(None, description="Id of an existing reference this photo is near-identical to.")
    best_existing_score: Optional[float] = None
    threshold: float = Field(..., description="Similarity at or above which two faces are treated as the same person.")


class AnalyzeImageRequest(BaseModel):
    image_base64: str = Field(..., description="Base64 JPEG/PNG/WebP, optionally a data: URI.")
    purpose: Literal["registration", "verification"] = "verification"


class FileInfo(BaseModel):
    format: Optional[str] = None
    bytes: int = 0
    width: int = 0
    height: int = 0


class FaceBox(BaseModel):
    x: int
    y: int
    w: int
    h: int
    confidence: float


class AnalyzeImageResponse(BaseModel):
    passed: bool
    purpose: str
    file: FileInfo
    analysis_size: Dict[str, int]
    face_count: int
    faces: List[FaceBox]
    metrics: Dict[str, Any]
    issues: List[str]
    warnings: List[str]
    messages: List[str]


class LivenessFrame(BaseModel):
    image_base64: str
    t_ms: int = Field(..., description="Capture time in milliseconds on the client's clock; only differences are used.")


class LivenessRequest(BaseModel):
    steps: List[Literal["turn_left", "turn_right"]] = Field(..., max_length=3, description="Empty means no head turns are asked.")
    frames: List[LivenessFrame] = Field(..., min_length=1, max_length=20)
    candidates: Optional[List[IdentifyCandidate]] = Field(None, max_length=20)


class LivenessResponse(BaseModel):
    frames_received: int
    frames_usable: int
    face_found_frac: float
    yaw_series: List[Optional[float]]
    neutral_frame_index: Optional[int] = None
    steps: List[Dict[str, Any]]
    challenge_completed: bool
    passive: Dict[str, Any]
    continuity: Dict[str, Any]
    duplicate_frame_frac: float
    timing: Dict[str, Any]
    identification: Optional[IdentifyResponse] = None
    neutral_quality: Optional[AnalyzeImageResponse] = None
    issues: List[str]


class HealthResponse(BaseModel):
    status: str
    model: str
    detector: Optional[str] = None
    threshold: Optional[float] = None
    anti_spoofing: Optional[bool] = Field(None, description="Whether passive anti-spoofing (MiniFASNet) is loaded; liveness cannot pass without it.")
