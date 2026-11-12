"""
Face embedding + comparison engine.

Uses DeepFace with the ArcFace model to turn an image into a numeric embedding,
and cosine similarity to compare two embeddings. No raw face images are ever
persisted by this service or by the caller (ChildGuard.Api) — only the
embedding vector is stored, per the project's "secure representation over raw
image storage" requirement.

Pipeline: decode -> detect + align the face (MTCNN finds the eyes and levels the
face, which keeps scores stable when the head is tilted) -> ArcFace embedding of
the largest face in the frame -> cosine similarity against the stored embedding.
"""
from __future__ import annotations

import base64
import json
import logging
import os
import threading
from typing import Optional

import cv2
import numpy as np
from deepface import DeepFace

logger = logging.getLogger("face_engine")

MODEL_NAME = "ArcFace"

# MTCNN ships its weights inside the pip package (works offline) and returns eye
# landmarks, so faces are aligned before embedding. Override with FACE_DETECTOR
# (e.g. "retinaface", "opencv") if needed. Changing the detector does not
# invalidate stored embeddings: they are ArcFace vectors either way.
DETECTOR_BACKEND = os.getenv("FACE_DETECTOR", "mtcnn")

# Cosine SIMILARITY (1.0 = identical) at or above which two faces are the same person.
# DeepFace's tuned ArcFace value is a cosine DISTANCE of 0.68, i.e. similarity 0.32.
# The school chose 0.40 (2026-11-07): a margin stricter than that published value
# that still accepts the normal difference between a phone selfie at registration
# and a desk webcam at pickup. An admin still approves every matched pickup.
MATCH_THRESHOLD = float(os.getenv("FACE_MATCH_THRESHOLD", "0.40"))

# Registration quality gates: a stored face is compared against for years, so it
# must be one clear, reasonably large face.
MIN_FACE_PX = int(os.getenv("FACE_MIN_SIZE_PX", "80"))
MIN_DETECTION_CONFIDENCE = 0.90
# Two registration photos this similar are treated as the same shot twice (or consecutive
# webcam frames); storing both adds nothing. Not calibrated on real data yet: the evaluation
# harness reports the same-person similarity spread. A near-identical pair is stored once,
# never rejected as an error.
DUPLICATE_SIMILARITY = float(os.getenv("FACE_DUPLICATE_SIMILARITY", "0.95"))
# A second face at least this fraction of the main face's area makes a
# registration ambiguous (whose face is being stored?).
SECOND_FACE_AREA_RATIO = 0.35

# TensorFlow models are shared; one inference at a time keeps memory flat and
# avoids races in the detector. The desk sends one capture per verification.
_lock = threading.Lock()
INFERENCE_LOCK = _lock

NO_FACE_MESSAGE = ("No face was found in the photo. Face the camera directly, in good light, "
                   "with nothing covering the face, and try again.")


class FaceEngineError(Exception):
    """Raised when an image cannot be used: undecodable, no face, or not good enough."""


def _decode_image(image_base64: str) -> np.ndarray:
    try:
        if "," in image_base64 and image_base64.strip().startswith("data:"):
            image_base64 = image_base64.split(",", 1)[1]
        raw = base64.b64decode(image_base64)
        arr = np.frombuffer(raw, dtype=np.uint8)
        img = cv2.imdecode(arr, cv2.IMREAD_COLOR)
    except Exception as exc:
        raise FaceEngineError("The image could not be read. Capture the photo again.") from exc
    if img is None:
        raise FaceEngineError("The image could not be read. Capture the photo again.")
    return img


def _faces(img: np.ndarray) -> list[dict]:
    """All faces found in the image, largest first."""
    with _lock:
        try:
            faces = DeepFace.represent(
                img_path=img,
                model_name=MODEL_NAME,
                detector_backend=DETECTOR_BACKEND,
                enforce_detection=True,
                align=True,
            )
        except ValueError as exc:
            # DeepFace raises ValueError when no face is detected.
            raise FaceEngineError(NO_FACE_MESSAGE) from exc
    if not faces:
        raise FaceEngineError(NO_FACE_MESSAGE)
    return sorted(faces, key=lambda f: f["facial_area"]["w"] * f["facial_area"]["h"], reverse=True)


def _area(face: dict) -> int:
    return face["facial_area"]["w"] * face["facial_area"]["h"]


def compute_embedding(image_base64: str, *, strict: bool = False) -> list[float]:
    """
    Returns the ArcFace embedding of the main (largest) face in the image.

    strict=True is used for registration: exactly one clear face, large enough
    and confidently detected. Verification accepts the largest face in the frame,
    because people standing behind the guardian at the desk are normal.
    """
    faces = _faces(_decode_image(image_base64))
    main = faces[0]

    if strict:
        area = main["facial_area"]
        confidence = main.get("face_confidence")
        if confidence is not None and confidence < MIN_DETECTION_CONFIDENCE:
            raise FaceEngineError(NO_FACE_MESSAGE)
        if min(area["w"], area["h"]) < MIN_FACE_PX:
            raise FaceEngineError("The face is too small in the photo. Move closer to the camera so the "
                                  "face fills more of the frame, then try again.")
        if len(faces) > 1 and _area(faces[1]) >= SECOND_FACE_AREA_RATIO * _area(main):
            raise FaceEngineError("More than one face is in the photo. Only the guardian should be in "
                                  "the frame when registering.")

    return main["embedding"]


def cosine_similarity(a: list[float], b: list[float]) -> float:
    va, vb = np.array(a, dtype=np.float64), np.array(b, dtype=np.float64)
    denom = np.linalg.norm(va) * np.linalg.norm(vb)
    if denom == 0:
        return 0.0
    return float(np.dot(va, vb) / denom)


def register_face(image_base64: str) -> tuple[str, str]:
    """Returns (embedding_json, model_name)."""
    embedding = compute_embedding(image_base64, strict=True)
    logger.info("Face registered (detector=%s)", DETECTOR_BACKEND)
    return json.dumps(embedding), MODEL_NAME


def verify_face(stored_embedding_json: str, image_base64: str) -> tuple[bool, float]:
    """Returns (match, similarity_score)."""
    try:
        stored_embedding = json.loads(stored_embedding_json)
    except ValueError as exc:
        raise FaceEngineError("The registered face data is unreadable. Register the face again.") from exc
    new_embedding = compute_embedding(image_base64)
    score = cosine_similarity(stored_embedding, new_embedding)
    match = score >= MATCH_THRESHOLD
    # Scores only (never images): what is needed to tune the threshold on real use.
    logger.info("Face verify: similarity=%.3f threshold=%.2f match=%s", score, MATCH_THRESHOLD, match)
    return match, score


def _usable_embedding(embedding_json: str, length: int) -> Optional[list]:
    try:
        embedding = json.loads(embedding_json)
    except (TypeError, ValueError):
        return None
    if not isinstance(embedding, list) or len(embedding) != length:
        return None
    return embedding


def embed_array(img: np.ndarray) -> list:
    """ArcFace embedding of the main (largest) face in an already-decoded image."""
    return _faces(img)[0]["embedding"]


def identify_face(image_base64: str, candidates: list) -> dict:
    """
    Scores one capture against every reference of every candidate guardian.
    Measures only: the caller decides what the scores mean. A reference that cannot be
    read is reported, not fatal, so one corrupt row does not block the others.
    """
    return score_candidates(compute_embedding(image_base64), candidates)


def score_candidates(new_embedding: list, candidates: list) -> dict:
    results = []
    for candidate in candidates:
        scored, unreadable = [], []
        for ref in candidate["references"]:
            stored = _usable_embedding(ref["embedding_json"], len(new_embedding))
            if stored is None:
                unreadable.append(ref["reference_id"])
                continue
            scored.append({"reference_id": ref["reference_id"], "score": cosine_similarity(stored, new_embedding)})
        best = max(scored, key=lambda s: s["score"]) if scored else None
        results.append({
            "guardian_id": candidate["guardian_id"],
            "best_score": best["score"] if best else None,
            "best_reference_id": best["reference_id"] if best else None,
            "scores": scored,
            "unreadable_reference_ids": unreadable,
        })
    logger.info("Face identify: candidates=%d best=%s threshold=%.2f", len(results),
                [None if r["best_score"] is None else round(r["best_score"], 3) for r in results], MATCH_THRESHOLD)
    return {"threshold": MATCH_THRESHOLD, "model": MODEL_NAME, "results": results}


def register_reference(image_base64: str, existing: list) -> dict:
    """
    Embeds one registration photo (same strict rules as register_face) and reports whether a
    near-identical reference already exists, so the same photo is not stored twice.
    """
    embedding = compute_embedding(image_base64, strict=True)
    duplicate_of, best_score = None, None
    for ref in existing:
        stored = _usable_embedding(ref["embedding_json"], len(embedding))
        if stored is None:
            continue
        score = cosine_similarity(stored, embedding)
        if best_score is None or score > best_score:
            best_score = score
            duplicate_of = ref["reference_id"] if score >= DUPLICATE_SIMILARITY else None
    logger.info("Face reference registered (detector=%s, best existing similarity=%s, duplicate=%s)",
                DETECTOR_BACKEND, None if best_score is None else round(best_score, 3), duplicate_of is not None)
    return {"embedding_json": json.dumps(embedding), "model": MODEL_NAME,
            "duplicate_of": duplicate_of, "best_existing_score": best_score, "threshold": MATCH_THRESHOLD}


def warm_up() -> None:
    """Loads ArcFace and the detector at startup so the first real request is not slow."""
    try:
        blank = np.full((160, 160, 3), 255, dtype=np.uint8)
        with _lock:
            DeepFace.represent(img_path=blank, model_name=MODEL_NAME,
                               detector_backend=DETECTOR_BACKEND, enforce_detection=False)
        logger.info("Face engine ready: model=%s detector=%s threshold=%.2f",
                    MODEL_NAME, DETECTOR_BACKEND, MATCH_THRESHOLD)
    except Exception:  # never block startup; the first request will load lazily instead
        logger.exception("Face engine warm-up failed")
