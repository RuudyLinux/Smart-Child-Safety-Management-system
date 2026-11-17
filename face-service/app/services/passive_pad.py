"""
Passive presentation-attack detection with the MiniFASNet pair that ships with DeepFace.

DeepFace's own `analyze` returns only the score of the *predicted* class and re-detects the face;
this reuses its loaded networks to return P(real) for a face box we already have. A face crop
at two scales goes through two small CNNs and the softmax outputs are averaged: class 1 is a real
face, classes 0 and 2 are the two spoof types the models were trained on (print and screen
captures from their training data, not necessarily the attacks seen at this school).

Fail-closed: if PyTorch or the weights cannot be loaded, `real_probability` returns None and the
caller must treat liveness as not passed. Single-frame score; the caller aggregates over several.
"""
from __future__ import annotations

import logging
import threading
from typing import Optional, Tuple

import numpy as np

logger = logging.getLogger("passive_pad")

_lock = threading.Lock()
_model = None
_load_error: Optional[str] = None


def _load():
    global _model, _load_error
    with _lock:
        if _model is not None:
            return _model
        if _load_error is not None:
            return None
        try:
            from deepface.modules import modeling
            _model = modeling.build_model(task="spoofing", model_name="Fasnet")
            logger.info("Passive anti-spoofing ready (MiniFASNet).")
        except Exception as exc:  # torch missing, or weights could not be downloaded
            _load_error = f"{type(exc).__name__}: {exc}"
            logger.exception("Passive anti-spoofing is unavailable; liveness will not pass.")
            return None
        return _model


def available() -> bool:
    return _load() is not None


def load_error() -> Optional[str]:
    return _load_error


def class_probabilities(img_bgr: np.ndarray, box: Tuple[int, int, int, int]) -> Optional[Tuple[float, float, float]]:
    """(P(spoof type 0), P(real), P(spoof type 2)) for the face at `box` (x, y, w, h), or None if unavailable."""
    model = _load()
    if model is None:
        return None

    import torch
    import torch.nn.functional as F
    from deepface.models.spoofing.FasNet import crop, to_tensor

    x, y, w, h = [int(v) for v in box]
    if w < 2 or h < 2:
        return None
    first = to_tensor(crop(img_bgr, (x, y, w, h), 2.7, 80, 80)).unsqueeze(0).to(model.device)
    second = to_tensor(crop(img_bgr, (x, y, w, h), 4, 80, 80)).unsqueeze(0).to(model.device)
    with _lock, torch.no_grad():
        probs = (F.softmax(model.first_model(first), dim=1) + F.softmax(model.second_model(second), dim=1)) / 2
    p = probs.cpu().numpy()[0]
    return float(p[0]), float(p[1]), float(p[2])


def real_probability(img_bgr: np.ndarray, box: Tuple[int, int, int, int]) -> Optional[float]:
    probs = class_probabilities(img_bgr, box)
    return None if probs is None else probs[1]
