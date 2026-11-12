"""Head pose estimated from the five MTCNN keypoints. Uncalibrated estimates, not measurements."""
from __future__ import annotations

import math

# Nose displacement of 0.7 inter-eye distances is treated as 90 degrees; chosen so a nose
# offset of 0.35 reads as about 30 degrees. Not fitted to ground-truth poses.
_YAW_FULL_SCALE = 0.7


def pose_metrics(face: dict) -> dict:
    eye_l, eye_r, nose = face["eye_l"], face["eye_r"], face["nose"]
    mouth_mid_y = (face["mouth_l"][1] + face["mouth_r"][1]) / 2.0
    eye_mid_x = (eye_l[0] + eye_r[0]) / 2.0
    eye_mid_y = (eye_l[1] + eye_r[1]) / 2.0
    eye_dist = math.hypot(eye_r[0] - eye_l[0], eye_r[1] - eye_l[1])
    if eye_dist < 1e-6:
        return {"yaw_ratio": 0.0, "yaw_deg_est": 0.0, "pitch_ratio": 0.0, "roll_deg": 0.0, "valid": False}

    yaw_ratio = (nose[0] - eye_mid_x) / eye_dist
    yaw_deg = math.degrees(math.asin(max(-1.0, min(1.0, yaw_ratio / _YAW_FULL_SCALE))))

    eye_to_mouth = mouth_mid_y - eye_mid_y
    pitch_ratio = (nose[1] - eye_mid_y) / eye_to_mouth if eye_to_mouth > 1e-6 else 0.0
    roll_deg = math.degrees(math.atan2(eye_r[1] - eye_l[1], eye_r[0] - eye_l[0]))

    return {
        "yaw_ratio": round(yaw_ratio, 4),
        "yaw_deg_est": round(yaw_deg, 1),
        "pitch_ratio": round(pitch_ratio, 4),
        "roll_deg": round(roll_deg, 1),
        "valid": True,
    }
