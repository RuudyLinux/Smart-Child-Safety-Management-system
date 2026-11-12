"""The route's schemas must import and accept analyze() output on the Python version we run (3.8)."""
import base64
import unittest
from unittest import mock

import cv2
import numpy as np

from app.models.schemas import AnalyzeImageRequest, AnalyzeImageResponse
from app.routes.analyze import analyze_image
from app.services import image_quality as iq


def _jpeg_b64(size=400):
    rng = np.random.default_rng(3)
    img = np.clip(np.full((size, size, 3), (140, 170, 210), np.int16) + rng.integers(-40, 41, (size, size, 3)), 0, 255)
    ok, buf = cv2.imencode(".jpg", img.astype(np.uint8))
    return base64.b64encode(buf.tobytes()).decode()


def _face():
    return {"box": (100, 80, 200, 260), "confidence": 0.99, "eye_l": (150, 180), "eye_r": (250, 180),
            "nose": (200, 215), "mouth_l": (167, 260), "mouth_r": (233, 260)}


class AnalyzeRoute(unittest.TestCase):
    def test_response_model_accepts_a_full_result(self):
        with mock.patch.object(iq, "_default_detector", return_value=lambda _img: [_face()]):
            response = analyze_image(AnalyzeImageRequest(image_base64=_jpeg_b64(), purpose="registration"))
        self.assertIsInstance(response, AnalyzeImageResponse)
        self.assertEqual(response.purpose, "registration")
        self.assertEqual(response.face_count, 1)
        self.assertEqual(response.file.format, "JPEG")

    def test_response_model_accepts_a_file_failure(self):
        response = analyze_image(AnalyzeImageRequest(image_base64="@@not-base64@@"))
        self.assertFalse(response.passed)
        self.assertEqual(response.issues, ["BAD_FORMAT"])
        self.assertEqual(response.metrics, {})

    def test_unknown_purpose_is_rejected_by_the_schema(self):
        with self.assertRaises(ValueError):
            AnalyzeImageRequest(image_base64="x", purpose="admin")


if __name__ == "__main__":
    unittest.main()
