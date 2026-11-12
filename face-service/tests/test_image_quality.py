"""Layer 1 tests. Detection is faked so these run without TensorFlow or model weights."""
import base64
import unittest

import cv2
import numpy as np

from app.services import image_quality as iq
from app.services.pose import pose_metrics

SKIN_BGR = (140, 170, 210)


def encode(img, ext=".jpg", quality=90):
    ok, buf = cv2.imencode(ext, img, [cv2.IMWRITE_JPEG_QUALITY, quality] if ext == ".jpg" else [])
    assert ok
    return base64.b64encode(buf.tobytes()).decode()


def face_keypoints(cx=300, cy=300, d=100):
    return {
        "eye_l": (cx - d // 2, cy - 20), "eye_r": (cx + d // 2, cy - 20),
        "nose": (cx, cy + 15), "mouth_l": (cx - d // 3, cy + 60), "mouth_r": (cx + d // 3, cy + 60),
    }


def fake_face(box=(200, 160, 200, 260), confidence=0.99, **kp):
    return {"box": box, "confidence": confidence, **face_keypoints(**kp)}


def textured_face_image(size=600, noise=40, base=SKIN_BGR, seed=1):
    rng = np.random.default_rng(seed)
    img = np.full((size, size, 3), base, dtype=np.int16)
    img += rng.integers(-noise, noise + 1, img.shape, dtype=np.int16)
    return np.clip(img, 0, 255).astype(np.uint8)


def detector_returning(*faces):
    return lambda _img: list(faces)


class FileValidation(unittest.TestCase):
    def test_garbage_base64_is_bad_format(self):
        r = iq.analyze("not base64 !!!", detector=detector_returning())
        self.assertEqual(r["issues"], ["BAD_FORMAT"])
        self.assertFalse(r["passed"])

    def test_non_image_bytes_are_bad_format_even_if_valid_base64(self):
        r = iq.analyze(base64.b64encode(b"MZ\x90\x00 this is an exe").decode(), detector=detector_returning())
        self.assertEqual(r["issues"], ["BAD_FORMAT"])

    def test_gif_is_refused_by_magic_bytes(self):
        r = iq.analyze(base64.b64encode(b"GIF89a" + b"\x00" * 50).decode(), detector=detector_returning())
        self.assertEqual(r["issues"], ["BAD_FORMAT"])

    def test_oversize_payload_is_refused_without_decoding(self):
        r = iq.analyze("A" * (iq.MAX_IMAGE_BYTES * 2), detector=detector_returning())
        self.assertEqual(r["issues"], ["TOO_LARGE"])

    def test_data_uri_prefix_is_accepted(self):
        img = textured_face_image()
        r = iq.analyze("data:image/jpeg;base64," + encode(img), detector=detector_returning(fake_face()))
        self.assertEqual(r["file"]["format"], "JPEG")

    def test_png_and_webp_are_accepted(self):
        img = textured_face_image()
        for ext, fmt in ((".png", "PNG"), (".webp", "WEBP")):
            r = iq.analyze(encode(img, ext), detector=detector_returning(fake_face()))
            self.assertEqual(r["file"]["format"], fmt, ext)

    def test_tiny_image_is_low_resolution(self):
        r = iq.analyze(encode(textured_face_image(size=100)), detector=detector_returning())
        self.assertEqual(r["issues"], ["LOW_RESOLUTION"])

    def test_oversized_dimensions_are_refused_before_decode(self):
        header_only = encode(np.zeros((16, 16, 3), np.uint8), ".png")
        original = iq.MAX_SIDE_PX
        iq.MAX_SIDE_PX = 8
        try:
            r = iq.analyze(header_only, detector=detector_returning())
        finally:
            iq.MAX_SIDE_PX = original
        self.assertEqual(r["issues"], ["TOO_LARGE"])


class FaceChecks(unittest.TestCase):
    def test_no_face(self):
        r = iq.analyze(encode(textured_face_image()), detector=detector_returning())
        self.assertEqual(r["issues"], ["NO_FACE"])
        self.assertEqual(r["face_count"], 0)

    def test_low_confidence_detection_counts_as_no_face(self):
        r = iq.analyze(encode(textured_face_image()), detector=detector_returning(fake_face(confidence=0.6)))
        self.assertEqual(r["issues"], ["NO_FACE"])

    def test_clean_single_face_passes(self):
        r = iq.analyze(encode(textured_face_image()), detector=detector_returning(fake_face()))
        self.assertEqual(r["issues"], [], r)
        self.assertTrue(r["passed"])
        self.assertEqual(r["face_count"], 1)

    def test_second_comparable_face_is_multiple_faces(self):
        r = iq.analyze(encode(textured_face_image()),
                       detector=detector_returning(fake_face(), fake_face(box=(10, 10, 180, 240), cx=100, cy=130)))
        self.assertIn("MULTIPLE_FACES", r["issues"])

    def test_small_background_face_is_ignored(self):
        r = iq.analyze(encode(textured_face_image()),
                       detector=detector_returning(fake_face(), fake_face(box=(10, 10, 60, 80), cx=40, cy=40, d=30)))
        self.assertNotIn("MULTIPLE_FACES", r["issues"])

    def test_small_face(self):
        r = iq.analyze(encode(textured_face_image()),
                       detector=detector_returning(fake_face(box=(250, 250, 60, 70), cx=280, cy=270, d=30)))
        self.assertIn("FACE_TOO_SMALL", r["issues"])

    def test_face_outside_frame_is_cut_off(self):
        r = iq.analyze(encode(textured_face_image()), detector=detector_returning(fake_face(box=(-80, 100, 200, 260))))
        self.assertIn("FACE_CUT_OFF", r["issues"])


class ImageQuality(unittest.TestCase):
    def test_blur_is_detected(self):
        blurred = cv2.GaussianBlur(textured_face_image(), (0, 0), 6)
        r = iq.analyze(encode(blurred), detector=detector_returning(fake_face()))
        self.assertIn("BLURRY", r["issues"])
        self.assertLess(r["metrics"]["sharpness"], iq.MIN_SHARPNESS)

    def test_dark_image(self):
        dark = (textured_face_image().astype(np.float32) * 0.15).astype(np.uint8)
        r = iq.analyze(encode(dark), detector=detector_returning(fake_face()))
        self.assertIn("TOO_DARK", r["issues"])

    def test_overexposed_image(self):
        bright = np.clip(textured_face_image().astype(np.int16) + 120, 0, 255).astype(np.uint8)
        r = iq.analyze(encode(bright), detector=detector_returning(fake_face()))
        self.assertIn("TOO_BRIGHT", r["issues"])

    def test_extreme_yaw(self):
        turned = fake_face()
        turned["nose"] = (turned["eye_r"][0] + 10, turned["nose"][1])
        r = iq.analyze(encode(textured_face_image()), detector=detector_returning(turned))
        self.assertIn("EXTREME_ANGLE", r["issues"])

    def test_extreme_roll(self):
        tilted = fake_face()
        tilted["eye_r"] = (tilted["eye_r"][0], tilted["eye_r"][1] + 90)
        r = iq.analyze(encode(textured_face_image()), detector=detector_returning(tilted))
        self.assertIn("EXTREME_ANGLE", r["issues"])


class Obstruction(unittest.TestCase):
    def _image_with_sunglasses(self):
        img = textured_face_image(noise=6)
        face = fake_face()
        for p in (face["eye_l"], face["eye_r"]):
            cv2.rectangle(img, (p[0] - 40, p[1] - 28), (p[0] + 40, p[1] + 28), (15, 15, 15), -1)
        return img, face

    def test_sunglasses_are_flagged(self):
        img, face = self._image_with_sunglasses()
        r = iq.analyze(encode(img, quality=95), detector=detector_returning(face))
        self.assertIn("FACE_OBSTRUCTED", r["issues"], r["metrics"])

    def test_blue_mask_only_warns(self):
        img = textured_face_image(noise=6)
        face = fake_face()
        cv2.rectangle(img, (230, 308), (370, 400), (200, 140, 40), -1)
        r = iq.analyze(encode(img, quality=95), detector=detector_returning(face))
        self.assertIn("FACE_OBSTRUCTED", r["warnings"], r["metrics"])
        self.assertNotIn("FACE_OBSTRUCTED", r["issues"])
        self.assertTrue(r["passed"])

    def test_plain_face_is_not_flagged(self):
        r = iq.analyze(encode(textured_face_image(noise=6), quality=95), detector=detector_returning(fake_face()))
        self.assertNotIn("FACE_OBSTRUCTED", r["issues"] + r["warnings"], r["metrics"])

    def test_low_contrast_blocks_registration_but_only_warns_for_verification(self):
        flat = textured_face_image(noise=18)
        encoded = encode(flat, quality=95)
        verification = iq.analyze(encoded, "verification", detector_returning(fake_face()))
        registration = iq.analyze(encoded, "registration", detector_returning(fake_face()))
        self.assertIn("LOW_CONTRAST", verification["warnings"])
        self.assertTrue(verification["passed"])
        self.assertIn("LOW_CONTRAST", registration["issues"])
        self.assertFalse(registration["passed"])


class Pose(unittest.TestCase):
    def test_frontal_face_has_near_zero_yaw(self):
        self.assertAlmostEqual(pose_metrics(fake_face())["yaw_deg_est"], 0.0, delta=1.0)

    def test_turn_direction_sign(self):
        right = fake_face()
        right["nose"] = (right["nose"][0] + 30, right["nose"][1])
        left = fake_face()
        left["nose"] = (left["nose"][0] - 30, left["nose"][1])
        self.assertGreater(pose_metrics(right)["yaw_deg_est"], 10)
        self.assertLess(pose_metrics(left)["yaw_deg_est"], -10)

    def test_coincident_eyes_are_invalid_not_a_crash(self):
        face = fake_face()
        face["eye_r"] = face["eye_l"]
        self.assertFalse(pose_metrics(face)["valid"])


if __name__ == "__main__":
    unittest.main()
