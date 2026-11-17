"""
Layer 1: is this image fit for face recognition?

Measures only. The caller (ChildGuard.Api) decides what blocks a verification. File
checks use magic bytes and Pillow's header read (never the extension, and never a full
decode of an oversized image). Nothing here stores an image.

Blur, exposure, angle and obstruction are heuristics on one frame. Angle comes from the
five MTCNN keypoints. Obstruction blocks only on very dark, flat eye regions (sunglasses);
a lower-face colour test (masks, scarves) only warns. Hands over the mouth, subtle
occlusion and unusual lighting can slip through, and make-up, beards or lipstick can raise
the warning on a clear face. A trained occlusion model would be needed for more.
"""
from __future__ import annotations

import base64
import binascii
import io
import math
import os
from typing import Callable, Optional

import cv2
import numpy as np
from PIL import Image, UnidentifiedImageError

from app.services.pose import pose_metrics

MAX_IMAGE_BYTES = int(os.getenv("FACE_MAX_IMAGE_BYTES", str(2 * 1024 * 1024)))
MAX_SIDE_PX = int(os.getenv("FACE_MAX_SIDE_PX", "4096"))
MIN_SIDE_PX = int(os.getenv("FACE_MIN_IMAGE_SIDE_PX", "160"))
ANALYSIS_MAX_SIDE_PX = 1280

MIN_FACE_PX = int(os.getenv("FACE_MIN_SIZE_PX", "80"))
MIN_DETECTION_CONFIDENCE = float(os.getenv("FACE_MIN_DETECTION_CONFIDENCE", "0.90"))
SECOND_FACE_AREA_RATIO = float(os.getenv("FACE_SECOND_FACE_AREA_RATIO", "0.35"))

MIN_SHARPNESS = float(os.getenv("FACE_MIN_SHARPNESS", "45"))
MIN_BRIGHTNESS = float(os.getenv("FACE_MIN_BRIGHTNESS", "55"))
MAX_BRIGHTNESS = float(os.getenv("FACE_MAX_BRIGHTNESS", "200"))
MAX_DARK_CLIPPED = float(os.getenv("FACE_MAX_DARK_CLIPPED_FRAC", "0.30"))
MAX_BRIGHT_CLIPPED = float(os.getenv("FACE_MAX_BRIGHT_CLIPPED_FRAC", "0.20"))
MIN_CONTRAST = float(os.getenv("FACE_MIN_CONTRAST", "22"))

MAX_YAW_DEG = float(os.getenv("FACE_MAX_YAW_DEG", "30"))
MAX_ROLL_DEG = float(os.getenv("FACE_MAX_ROLL_DEG", "30"))
PITCH_RATIO_RANGE = (float(os.getenv("FACE_MIN_PITCH_RATIO", "0.30")), float(os.getenv("FACE_MAX_PITCH_RATIO", "1.00")))
CUT_OFF_FRACTION = 0.05

_CHROMA_TAU = 14.0
_EYE_DARK_STRONG, _EYE_STD_STRONG = 0.40, 22.0
_EYE_DARK_SOFT, _EYE_STD_SOFT = 0.55, 30.0
_MASK_SOFT = 0.60

MESSAGES = {
    "BAD_FORMAT": "The file is not a valid JPEG, PNG or WebP image. Choose a photo or capture again.",
    "TOO_LARGE": "The image is too large. Use a photo under 2 MB and no larger than 4096 pixels on a side.",
    "LOW_RESOLUTION": "The image resolution is too low. Use a larger photo.",
    "NO_FACE": "No clear face was found. Face the camera directly, in good light, with nothing covering the face.",
    "MULTIPLE_FACES": "More than one face was detected. Only the guardian should be in the frame.",
    "FACE_TOO_SMALL": "The face is too small in the picture. Move closer so the face fills more of the frame.",
    "FACE_CUT_OFF": "Part of the face is outside the picture. Centre the face in the frame.",
    "BLURRY": "The image is too blurry. Hold the camera steady and keep the face in focus.",
    "TOO_DARK": "The face is too dark. Move to better light, with the light in front of the face rather than behind it.",
    "TOO_BRIGHT": "The face is overexposed. Avoid glare and direct bright light on the face.",
    "LOW_CONTRAST": "The lighting is flat or uneven. Use even, front-facing light.",
    "EXTREME_ANGLE": "The face is turned or tilted too far. Face the camera directly.",
    "FACE_OBSTRUCTED": "Part of the face looks covered (sunglasses, a mask or similar). Remove it and try again.",
}

# Registration photos are compared against for years, so flat lighting blocks there.
_PROMOTED_FOR_REGISTRATION = ("LOW_CONTRAST",)

Detector = Callable[[np.ndarray], list]


def _default_detector() -> Detector:
    from app.services.face_detection import detect
    return detect


def _decode(image_base64: str) -> tuple[Optional[np.ndarray], dict, list[str]]:
    info: dict = {"format": None, "bytes": 0, "width": 0, "height": 0}
    text = image_base64.strip()
    if text.startswith("data:") and "," in text:
        text = text.split(",", 1)[1]
    text = "".join(text.split())

    if len(text) * 3 // 4 > MAX_IMAGE_BYTES + 4:
        info["bytes"] = len(text) * 3 // 4
        return None, info, ["TOO_LARGE"]
    try:
        raw = base64.b64decode(text, validate=True)
    except (binascii.Error, ValueError):
        return None, info, ["BAD_FORMAT"]
    info["bytes"] = len(raw)
    if not raw:
        return None, info, ["BAD_FORMAT"]
    if len(raw) > MAX_IMAGE_BYTES:
        return None, info, ["TOO_LARGE"]

    if raw[:3] == b"\xff\xd8\xff":
        magic = "JPEG"
    elif raw[:8] == b"\x89PNG\r\n\x1a\n":
        magic = "PNG"
    elif raw[:4] == b"RIFF" and raw[8:12] == b"WEBP":
        magic = "WEBP"
    else:
        return None, info, ["BAD_FORMAT"]

    try:
        with Image.open(io.BytesIO(raw)) as header:
            width, height = header.size
            if header.format != magic:
                return None, info, ["BAD_FORMAT"]
    except (UnidentifiedImageError, OSError, ValueError):
        return None, info, ["BAD_FORMAT"]
    info.update(format=magic, width=width, height=height)

    if width > MAX_SIDE_PX or height > MAX_SIDE_PX:
        return None, info, ["TOO_LARGE"]

    img = cv2.imdecode(np.frombuffer(raw, dtype=np.uint8), cv2.IMREAD_COLOR)
    if img is None:
        return None, info, ["BAD_FORMAT"]
    info.update(width=img.shape[1], height=img.shape[0])
    if min(img.shape[:2]) < MIN_SIDE_PX:
        return img, info, ["LOW_RESOLUTION"]
    return img, info, []


def _crop(arr: np.ndarray, x0: float, y0: float, x1: float, y1: float) -> np.ndarray:
    h, w = arr.shape[:2]
    return arr[max(0, int(y0)):min(h, int(y1)), max(0, int(x0)):min(w, int(x1))]


def _obstruction(img: np.ndarray, gray: np.ndarray, face: dict, face_mean: float) -> dict:
    eye_l, eye_r, nose = face["eye_l"], face["eye_r"], face["nose"]
    d = math.hypot(eye_r[0] - eye_l[0], eye_r[1] - eye_l[1])
    out: dict = {"eye_dark_ratio": None, "eye_std": None, "lower_face_chroma_dev_frac": None,
                 "strong": False, "suspect": False}
    if d < 1.0:
        return out

    half = max(3.0, 0.22 * d)
    patches = [_crop(gray, p[0] - half, p[1] - half, p[0] + half, p[1] + half) for p in (eye_l, eye_r)]
    if all(p.size for p in patches):
        out["eye_dark_ratio"] = round(float(max(p.mean() for p in patches)) / max(face_mean, 1.0), 3)
        out["eye_std"] = round(float(max(p.std() for p in patches)), 1)

    ycrcb = cv2.cvtColor(img, cv2.COLOR_BGR2YCrCb)
    # Skin reference: the strip just under each eye. Masks and scarves sit below it, and it
    # is not hair (unlike the forehead, where a fringe would make the reference wrong).
    cheeks = [_crop(ycrcb, p[0] - 0.2 * d, p[1] + 0.08 * d, p[0] + 0.2 * d, p[1] + 0.22 * d)
              for p in (eye_l, eye_r)]
    cheeks = [c.reshape(-1, 3) for c in cheeks if c.size]
    mouth_y = (face["mouth_l"][1] + face["mouth_r"][1]) / 2.0
    lower = _crop(ycrcb, min(face["mouth_l"][0], face["mouth_r"][0]) - 0.1 * d, nose[1] + 0.3 * (mouth_y - nose[1]),
                  max(face["mouth_l"][0], face["mouth_r"][0]) + 0.1 * d, mouth_y + 0.55 * d)
    if cheeks and lower.size:
        ref = np.concatenate(cheeks)
        ref_cr, ref_cb = np.median(ref[:, 1]), np.median(ref[:, 2])
        dev = np.hypot(lower[:, :, 1].astype(np.float32) - ref_cr, lower[:, :, 2].astype(np.float32) - ref_cb)
        out["lower_face_chroma_dev_frac"] = round(float((dev > _CHROMA_TAU).mean()), 3)

    ratio, eye_std, mask = out["eye_dark_ratio"], out["eye_std"], out["lower_face_chroma_dev_frac"]
    glasses_strong = ratio is not None and ratio < _EYE_DARK_STRONG and eye_std < _EYE_STD_STRONG
    glasses_soft = ratio is not None and ratio < _EYE_DARK_SOFT and eye_std < _EYE_STD_SOFT
    # Only the sunglasses test may block. The lower-face colour test raised a strong finding on
    # a plain face with heavy eye make-up and lipstick, so it can only warn until it is
    # calibrated on real data.
    out["strong"] = bool(glasses_strong)
    out["suspect"] = bool(glasses_soft or (mask is not None and mask > _MASK_SOFT))
    return out


def analyze(image_base64: str, purpose: str = "verification", detector: Optional[Detector] = None) -> dict:
    img, file_info, issues = _decode(image_base64)
    result: dict = {
        "passed": False, "purpose": purpose, "file": file_info,
        "analysis_size": {"width": 0, "height": 0},
        "face_count": 0, "faces": [], "metrics": {}, "issues": [], "warnings": [], "messages": [],
    }
    if img is None or issues:
        result["issues"] = issues
        result["messages"] = [MESSAGES[c] for c in issues]
        return result

    longest = max(img.shape[:2])
    if longest > ANALYSIS_MAX_SIDE_PX:
        scale = ANALYSIS_MAX_SIDE_PX / longest
        img = cv2.resize(img, (int(img.shape[1] * scale), int(img.shape[0] * scale)), interpolation=cv2.INTER_AREA)
    ih, iw = img.shape[:2]
    result["analysis_size"] = {"width": iw, "height": ih}

    detected = (detector or _default_detector())(img)
    good = [f for f in detected if f["confidence"] >= MIN_DETECTION_CONFIDENCE]
    result["face_count"] = len(good)
    result["faces"] = [{"x": f["box"][0], "y": f["box"][1], "w": f["box"][2], "h": f["box"][3],
                        "confidence": round(f["confidence"], 3)} for f in good[:5]]
    if not good:
        result["issues"] = ["NO_FACE"]
        result["messages"] = [MESSAGES["NO_FACE"]]
        return result

    main = good[0]
    x, y, w, h = main["box"]
    found: list[str] = []
    warn: list[str] = []

    area = w * h
    if any(f["box"][2] * f["box"][3] >= SECOND_FACE_AREA_RATIO * area for f in good[1:]):
        found.append("MULTIPLE_FACES")
    if min(w, h) < MIN_FACE_PX:
        found.append("FACE_TOO_SMALL")
    slack = CUT_OFF_FRACTION * max(w, h)
    if x < -slack or y < -slack or x + w > iw + slack or y + h > ih + slack:
        found.append("FACE_CUT_OFF")

    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    pad_w, pad_h = 0.1 * w, 0.1 * h
    face_gray = _crop(gray, x - pad_w, y - pad_h, x + w + pad_w, y + h + pad_h)
    inner = _crop(gray, x + 0.2 * w, y + 0.2 * h, x + 0.8 * w, y + 0.8 * h)
    if face_gray.size == 0 or inner.size == 0:
        found.append("NO_FACE")
        result["issues"] = sorted(set(found))
        result["messages"] = [MESSAGES[c] for c in result["issues"]]
        return result

    side = cv2.resize(face_gray, (128, 128), interpolation=cv2.INTER_AREA if min(face_gray.shape) >= 128 else cv2.INTER_LINEAR)
    sharpness = float(cv2.Laplacian(side, cv2.CV_64F).var())
    brightness = float(inner.mean())
    contrast = float(inner.std())
    dark_frac = float((face_gray <= 10).mean())
    bright_frac = float((face_gray >= 245).mean())
    pose = pose_metrics(main)
    obstruction = _obstruction(img, gray, main, brightness)

    if sharpness < MIN_SHARPNESS:
        found.append("BLURRY")
    if brightness < MIN_BRIGHTNESS or dark_frac > MAX_DARK_CLIPPED:
        found.append("TOO_DARK")
    if brightness > MAX_BRIGHTNESS or bright_frac > MAX_BRIGHT_CLIPPED:
        found.append("TOO_BRIGHT")
    if contrast < MIN_CONTRAST:
        warn.append("LOW_CONTRAST")
    if pose["valid"] and (abs(pose["yaw_deg_est"]) > MAX_YAW_DEG or abs(pose["roll_deg"]) > MAX_ROLL_DEG
                          or not (PITCH_RATIO_RANGE[0] <= pose["pitch_ratio"] <= PITCH_RATIO_RANGE[1])):
        found.append("EXTREME_ANGLE")
    if obstruction["strong"]:
        found.append("FACE_OBSTRUCTED")
    elif obstruction["suspect"]:
        warn.append("FACE_OBSTRUCTED")

    if purpose == "registration":
        found.extend(c for c in warn if c in _PROMOTED_FOR_REGISTRATION)
        warn = [c for c in warn if c not in _PROMOTED_FOR_REGISTRATION]

    found = list(dict.fromkeys(found))
    warn = [c for c in dict.fromkeys(warn) if c not in found]
    result["metrics"] = {
        "sharpness": round(sharpness, 1), "brightness": round(brightness, 1), "contrast": round(contrast, 1),
        "clipped_dark_frac": round(dark_frac, 3), "clipped_bright_frac": round(bright_frac, 3),
        "face_px": int(min(w, h)), "detection_confidence": round(main["confidence"], 3),
        **pose, "obstruction": obstruction,
    }
    result["issues"] = found
    result["warnings"] = warn
    result["messages"] = [MESSAGES[c] for c in found] + [MESSAGES[c] for c in warn]
    result["passed"] = not found
    return result
