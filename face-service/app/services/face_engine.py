"""
Face embedding + comparison engine.

Uses DeepFace with the ArcFace model to turn an image into a numeric embedding,
and cosine similarity to compare two embeddings. No raw face images are ever
persisted by this service or by the caller (ChildGuard.Api) — only the
embedding vector is stored, per the project's "secure representation over raw
image storage" requirement.
"""
from __future__ import annotations

import base64
import json
import logging

import cv2
import numpy as np
from deepface import DeepFace

logger = logging.getLogger("face_engine")

MODEL_NAME = "ArcFace"
DETECTOR_BACKEND = "opencv"
# Cosine-similarity threshold above which two embeddings are considered the same person.
# ArcFace's own published verification threshold (DeepFace default) is ~0.68 distance;
# expressed here as similarity so higher = more similar.
MATCH_THRESHOLD = 0.68


class FaceEngineError(Exception):
    """Raised when an image cannot be decoded or no face can be detected in it."""


def _decode_image(image_base64: str) -> np.ndarray:
    try:
        if "," in image_base64 and image_base64.strip().startswith("data:"):
            image_base64 = image_base64.split(",", 1)[1]
        raw = base64.b64decode(image_base64)
        arr = np.frombuffer(raw, dtype=np.uint8)
        img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
        if img is None:
            raise FaceEngineError("Could not decode image data.")
        return img
    except Exception as exc:
        raise FaceEngineError(f"Invalid image data: {exc}") from exc


def compute_embedding(image_base64: str) -> list[float]:
    """Detects the face in the image and returns its ArcFace embedding."""
    img = _decode_image(image_base64)
    try:
        result = DeepFace.represent(
            img_path=img,
            model_name=MODEL_NAME,
            detector_backend=DETECTOR_BACKEND,
            enforce_detection=True,
        )
    except ValueError as exc:
        # DeepFace raises ValueError when no face is detected.
        raise FaceEngineError(f"No face detected: {exc}") from exc

    if not result:
        raise FaceEngineError("No face detected in image.")
    return result[0]["embedding"]


def cosine_similarity(a: list[float], b: list[float]) -> float:
    va, vb = np.array(a), np.array(b)
    denom = (np.linalg.norm(va) * np.linalg.norm(vb))
    if denom == 0:
        return 0.0
    return float(np.dot(va, vb) / denom)


def register_face(image_base64: str) -> tuple[str, str]:
    """Returns (embedding_json, model_name)."""
    embedding = compute_embedding(image_base64)
    return json.dumps(embedding), MODEL_NAME


def verify_face(stored_embedding_json: str, image_base64: str) -> tuple[bool, float]:
    """Returns (match, similarity_score)."""
    stored_embedding = json.loads(stored_embedding_json)
    new_embedding = compute_embedding(image_base64)
    score = cosine_similarity(stored_embedding, new_embedding)
    return score >= MATCH_THRESHOLD, score
