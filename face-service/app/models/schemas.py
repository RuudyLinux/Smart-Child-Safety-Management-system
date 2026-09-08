"""Request/response schemas for the face-recognition service."""
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


class HealthResponse(BaseModel):
    status: str
    model: str
