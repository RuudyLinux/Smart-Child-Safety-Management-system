"""
Liveness signals from a short webcam burst: a head-turn challenge, passive anti-spoofing and
face continuity. Measures only; ChildGuard.Api decides what counts as a pass.

What a frame sequence can and cannot show:
  * A person who really turns their head through the requested sequence produces yaw changes in
    the requested order. A held-up photo or a looped video that does not match the (random,
    single-use, server-issued) sequence does not.
  * MiniFASNet scores each sampled frame as real or spoof (print/screen) on its own.
  * ArcFace on the frontal and the turned frames checks the same face is present throughout.
None of this is proof against a determined attacker (a live video of the guardian that can be
steered through the requested turns, a 3D mask). Thresholds here are defaults that have not been
calibrated on real webcams; see the evaluation report for what was measured.

Direction convention: the person's own left means the nose moves toward the RIGHT of the raw,
unmirrored camera frame (yaw_ratio > 0). A mirrored preview shows that as movement to the left,
which is what the person sees and is told.
"""
from __future__ import annotations

import hashlib
import logging
import os
from typing import Callable, Dict, List, Optional

import numpy as np

from app.services import image_quality as iq
from app.services.pose import pose_metrics

logger = logging.getLogger("liveness")

STEP_DIRECTIONS = {"turn_left": 1, "turn_right": -1}

MIN_FRAMES = int(os.getenv("LIVENESS_MIN_FRAMES", "6"))
MAX_FRAMES = int(os.getenv("LIVENESS_MAX_FRAMES", "16"))
TURN_MIN_RATIO = float(os.getenv("LIVENESS_TURN_MIN_RATIO", "0.20"))
RETURN_MAX_RATIO = float(os.getenv("LIVENESS_RETURN_MAX_RATIO", "0.12"))
NEUTRAL_MAX_RATIO = float(os.getenv("LIVENESS_NEUTRAL_MAX_RATIO", "0.12"))
NEUTRAL_SEARCH_FRAMES = 4
MIN_FACE_FOUND_FRAC = float(os.getenv("LIVENESS_MIN_FACE_FOUND_FRAC", "0.70"))
PAD_MAX_FRAMES = int(os.getenv("LIVENESS_PAD_FRAMES", "5"))
PAD_REAL_AT = 0.5
CONTINUITY_MIN = float(os.getenv("LIVENESS_CONTINUITY_MIN", "0.25"))
MIN_SPAN_MS = int(os.getenv("LIVENESS_MIN_SPAN_MS", "1500"))
MAX_SPAN_MS = int(os.getenv("LIVENESS_MAX_SPAN_MS", "15000"))
MAX_DUPLICATE_FRAC = float(os.getenv("LIVENESS_MAX_DUPLICATE_FRAC", "0.30"))
MAX_MULTI_FACE_FRAC = 0.20

Detector = Callable[[np.ndarray], list]
PadScorer = Callable[[np.ndarray, tuple], Optional[float]]
Embedder = Callable[[np.ndarray], list]
Scorer = Callable[[list, list], dict]


def _defaults():
    from app.services import face_engine, passive_pad
    from app.services.face_detection import detect
    return detect, passive_pad.real_probability, face_engine.embed_array, face_engine.score_candidates


def _cosine(a: list, b: list) -> float:
    va, vb = np.array(a, dtype=np.float64), np.array(b, dtype=np.float64)
    denom = np.linalg.norm(va) * np.linalg.norm(vb)
    return 0.0 if denom == 0 else float(np.dot(va, vb) / denom)


def detect_steps(steps: List[str], rel: List[Optional[float]], neutral_index: int) -> dict:
    """
    Walks the relative-yaw series once, in order. A step is done when the head turns far enough the
    requested way after the previous step finished and the head came back toward centre; turning
    far the wrong way first fails the challenge (so shaking the head both ways does not pass).
    """
    results: List[dict] = []
    cursor = neutral_index + 1
    wrong_direction = False
    blocked = False
    for step in steps:
        sign = STEP_DIRECTIONS[step]
        entry = {"step": step, "completed": False, "frame_index": None, "peak_rel_yaw": None}
        results.append(entry)
        if blocked:
            continue

        found = None
        for i in range(cursor, len(rel)):
            r = rel[i]
            if r is None:
                continue
            if sign * r >= TURN_MIN_RATIO:
                found = i
                break
            if -sign * r >= TURN_MIN_RATIO:
                wrong_direction = True
                break
        if found is None:
            blocked = True
            continue

        back = next((j for j in range(found + 1, len(rel)) if rel[j] is not None and abs(rel[j]) <= RETURN_MAX_RATIO), None)
        window_end = back if back is not None else len(rel)
        peak = max((i for i in range(found, window_end) if rel[i] is not None), key=lambda i: sign * rel[i])
        entry.update(completed=True, frame_index=peak, peak_rel_yaw=round(rel[peak], 3))
        cursor = (back + 1) if back is not None else len(rel)

    return {"steps": results, "completed": all(s["completed"] for s in results), "wrong_direction": wrong_direction}


def analyze_liveness(
    steps: List[str],
    frames: List[dict],
    candidates: Optional[list] = None,
    *,
    detector: Optional[Detector] = None,
    pad: Optional[PadScorer] = None,
    embed: Optional[Embedder] = None,
    scorer: Optional[Scorer] = None,
) -> dict:
    if len(steps) > 3 or any(s not in STEP_DIRECTIONS for s in steps):
        raise ValueError("The challenge must be at most three turn_left / turn_right steps.")
    if not (MIN_FRAMES <= len(frames) <= MAX_FRAMES):
        raise ValueError(f"Send between {MIN_FRAMES} and {MAX_FRAMES} frames.")

    if detector is None or pad is None or embed is None or scorer is None:
        d, p, e, s = _defaults()
        detector, pad, embed, scorer = detector or d, pad or p, embed or e, scorer or s

    issues: List[str] = []
    images: List[Optional[np.ndarray]] = []
    mains: List[Optional[dict]] = []
    all_faces: List[list] = []
    yaw: List[Optional[float]] = []
    seen_hashes = set()
    duplicates = 0
    multi_face = 0

    for frame in frames:
        digest = hashlib.sha1("".join(frame["image_base64"].split()).encode()).hexdigest()
        duplicates += digest in seen_hashes
        seen_hashes.add(digest)

        img, _info, problems = iq._decode(frame["image_base64"])
        if img is None or problems:
            images.append(None); mains.append(None); all_faces.append([]); yaw.append(None)
            continue
        longest = max(img.shape[:2])
        if longest > iq.ANALYSIS_MAX_SIDE_PX:
            import cv2
            scale = iq.ANALYSIS_MAX_SIDE_PX / longest
            img = cv2.resize(img, (int(img.shape[1] * scale), int(img.shape[0] * scale)), interpolation=cv2.INTER_AREA)

        faces = [f for f in detector(img) if f["confidence"] >= iq.MIN_DETECTION_CONFIDENCE]
        images.append(img)
        all_faces.append(faces)
        main = faces[0] if faces else None
        mains.append(main)
        if main is not None:
            area = main["box"][2] * main["box"][3]
            if any(f["box"][2] * f["box"][3] >= iq.SECOND_FACE_AREA_RATIO * area for f in faces[1:]):
                multi_face += 1
            pose = pose_metrics(main)
            yaw.append(pose["yaw_ratio"] if pose["valid"] else None)
        else:
            yaw.append(None)

    decoded = sum(i is not None for i in images)
    found = sum(m is not None for m in mains)
    face_found_frac = found / decoded if decoded else 0.0
    if decoded < MIN_FRAMES:
        issues.append("TOO_FEW_USABLE_FRAMES")
    elif face_found_frac < MIN_FACE_FOUND_FRAC:
        issues.append("FACE_LOST")
    if found and multi_face / found > MAX_MULTI_FACE_FRAC:
        issues.append("MULTIPLE_FACES")
    duplicate_frac = duplicates / len(frames)
    if duplicate_frac > MAX_DUPLICATE_FRAC:
        issues.append("DUPLICATE_FRAMES")

    times = [f["t_ms"] for f in frames]
    gaps = [b - a for a, b in zip(times, times[1:])]
    span = times[-1] - times[0]
    if any(g <= 0 for g in gaps) or span < MIN_SPAN_MS or span > MAX_SPAN_MS:
        issues.append("BAD_TIMING")

    neutral_index = None
    early = [i for i, y in enumerate(yaw) if y is not None][:NEUTRAL_SEARCH_FRAMES]
    if early:
        best = min(early, key=lambda i: abs(yaw[i]))
        if abs(yaw[best]) <= NEUTRAL_MAX_RATIO:
            neutral_index = best
    if neutral_index is None:
        issues.append("NO_NEUTRAL_FRAME")

    rel = [None if y is None or neutral_index is None else y - yaw[neutral_index] for y in yaw]
    challenge = (detect_steps(steps, rel, neutral_index) if neutral_index is not None
                 else {"steps": [{"step": s, "completed": False, "frame_index": None, "peak_rel_yaw": None} for s in steps],
                       "completed": False, "wrong_direction": False})
    if challenge["wrong_direction"]:
        issues.append("WRONG_DIRECTION")
    if not challenge["completed"]:
        issues.append("CHALLENGE_NOT_COMPLETED")

    # Passive anti-spoofing on a few frames spread across the burst, always including the neutral one.
    usable = [i for i in range(len(frames)) if mains[i] is not None]
    pick: List[int] = []
    if usable:
        step = max(1, len(usable) // PAD_MAX_FRAMES)
        pick = usable[::step][:PAD_MAX_FRAMES]
        if neutral_index is not None and neutral_index not in pick:
            pick = [neutral_index] + pick[:PAD_MAX_FRAMES - 1]
    scores: List[float] = []
    pad_available = bool(pick)
    for i in pick:
        value = pad(images[i], mains[i]["box"])
        if value is None:
            pad_available = False
            break
        scores.append(round(float(value), 4))
    passive = {
        "available": pad_available, "frames_scored": len(scores), "real_scores": scores,
        "real_frac": round(sum(s >= PAD_REAL_AT for s in scores) / len(scores), 3) if scores else None,
        "mean": round(sum(scores) / len(scores), 4) if scores else None,
        "min": round(min(scores), 4) if scores else None,
    }
    if not pad_available:
        issues.append("PASSIVE_UNAVAILABLE")

    # Same face throughout, and the frontal frame scored against the candidates.
    continuity = {"checked": 0, "min_cosine": None, "same_person": None}
    identification = None
    neutral_quality = None
    if neutral_index is not None:
        try:
            neutral_embedding = embed(images[neutral_index])
            others = sorted({s["frame_index"] for s in challenge["steps"] if s["completed"]} - {neutral_index})
            if not steps:  # no head turns asked: the same face must stay in view until the last frame
                last = [i for i in usable if i > neutral_index]
                others = last[-1:]
            sims = []
            for i in others:
                sims.append(_cosine(neutral_embedding, embed(images[i])))
            if sims:
                continuity = {"checked": len(sims), "min_cosine": round(min(sims), 4), "same_person": min(sims) >= CONTINUITY_MIN}
            if candidates:
                identification = scorer(neutral_embedding, candidates)
        except Exception:  # an embedding failure must never look like a pass
            logger.exception("Liveness embedding failed")
            issues.append("EMBEDDING_FAILED")
        neutral_quality = iq.analyze(frames[neutral_index]["image_base64"], "verification",
                                     detector=lambda _img, f=all_faces[neutral_index]: f)
    if continuity["same_person"] is False:
        issues.append("FACE_CHANGED")

    return {
        "frames_received": len(frames), "frames_usable": decoded, "face_found_frac": round(face_found_frac, 3),
        "yaw_series": [None if y is None else round(y, 3) for y in yaw],
        "neutral_frame_index": neutral_index,
        "steps": challenge["steps"], "challenge_completed": challenge["completed"],
        "passive": passive, "continuity": continuity,
        "duplicate_frame_frac": round(duplicate_frac, 3),
        "timing": {"span_ms": span, "min_gap_ms": min(gaps) if gaps else 0},
        "identification": identification, "neutral_quality": neutral_quality,
        "issues": list(dict.fromkeys(issues)),
    }
