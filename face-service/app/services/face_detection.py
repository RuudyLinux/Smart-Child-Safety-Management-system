"""
MTCNN face + 5-point landmark detection.

Reuses the MTCNN instance DeepFace already built for the embedding pipeline (no second
model load) but reads the nose and mouth keypoints as well, which DeepFace's own wrapper
drops. Image-space naming: eye_l is the eye on the left of the picture.
"""
from __future__ import annotations

import numpy as np
from deepface.modules import modeling

from app.services.face_engine import INFERENCE_LOCK


def detect(img_bgr: np.ndarray) -> list[dict]:
    """All faces found, largest first: {box:(x,y,w,h), confidence, eye_l, eye_r, nose, mouth_l, mouth_r}."""
    rgb = np.ascontiguousarray(img_bgr[:, :, ::-1])
    detector = modeling.build_model(task="face_detector", model_name="mtcnn").model
    with INFERENCE_LOCK:
        raw = detector.detect_faces(rgb)

    faces = []
    for d in raw or []:
        x, y, w, h = d["box"]
        kp = d["keypoints"]
        faces.append({
            "box": (int(x), int(y), int(w), int(h)),
            "confidence": float(d["confidence"]),
            "eye_l": tuple(kp["left_eye"]),
            "eye_r": tuple(kp["right_eye"]),
            "nose": tuple(kp["nose"]),
            "mouth_l": tuple(kp["mouth_left"]),
            "mouth_r": tuple(kp["mouth_right"]),
        })
    return sorted(faces, key=lambda f: f["box"][2] * f["box"][3], reverse=True)
