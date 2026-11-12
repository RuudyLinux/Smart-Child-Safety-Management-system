"""Liveness signal logic with detection, anti-spoofing and embedding faked. They check the challenge
arithmetic and the fail-closed paths, not how well MiniFASNet or ArcFace perform."""
import base64
import unittest
from unittest import mock

import cv2
import numpy as np

from app.services import liveness as lv

SKIN = (140, 170, 210)


def frame_image(seed):
    rng = np.random.default_rng(seed)
    img = np.clip(np.full((300, 400, 3), SKIN, np.int16) + rng.integers(-40, 41, (300, 400, 3)), 0, 255).astype(np.uint8)
    ok, buf = cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, 92])
    return base64.b64encode(buf.tobytes()).decode()


def face(yaw_ratio):
    return {"box": (80, 60, 140, 160), "confidence": 0.99,
            "eye_l": (100, 100), "eye_r": (200, 100), "nose": (150 + yaw_ratio * 100, 130),
            "mouth_l": (120, 170), "mouth_r": (180, 170)}


class Burst:
    """A frame sequence plus a detector that reports one yaw per frame (None = no face)."""

    def __init__(self, yaws, step_ms=400, images=None):
        self.yaws = list(yaws)
        self.frames = [{"image_base64": images[i] if images else frame_image(i), "t_ms": i * step_ms} for i in range(len(self.yaws))]
        self._calls = 0

    def detector(self, _img):
        y = self.yaws[self._calls]
        self._calls += 1
        return [] if y is None else [face(y)]


def run(burst, steps=("turn_left", "turn_right"), pad_value=0.95, embeddings=None, candidates=None, scorer=None, pad=None, embed=None):
    embedder = embed or (lambda _img: [1.0, 0.0, 0.0])
    return lv.analyze_liveness(
        list(steps), burst.frames, candidates,
        detector=burst.detector,
        pad=pad or (lambda _img, _box: pad_value),
        embed=embedder,
        scorer=scorer or (lambda emb, cands: {"threshold": 0.4, "model": "m", "results": []}),
    )


LEFT_THEN_RIGHT = [0.0, 0.02, 0.0, 0.1, 0.30, 0.38, 0.25, 0.05, -0.1, -0.30, -0.38, -0.05, 0.0]
RIGHT_THEN_LEFT = [-v for v in LEFT_THEN_RIGHT]


class ChallengeSequence(unittest.TestCase):
    def test_left_then_right_completes(self):
        r = run(Burst(LEFT_THEN_RIGHT))
        self.assertTrue(r["challenge_completed"], r)
        self.assertEqual([s["completed"] for s in r["steps"]], [True, True])
        self.assertEqual(r["steps"][0]["frame_index"], 5)   # peak of the left turn
        self.assertEqual(r["steps"][1]["frame_index"], 10)  # peak of the right turn
        self.assertEqual(r["issues"], [])

    def test_the_wrong_order_fails_with_wrong_direction(self):
        r = run(Burst(LEFT_THEN_RIGHT), steps=("turn_right", "turn_left"))
        self.assertFalse(r["challenge_completed"])
        self.assertIn("WRONG_DIRECTION", r["issues"])
        self.assertIn("CHALLENGE_NOT_COMPLETED", r["issues"])

    def test_the_mirror_sequence_matches_the_mirror_challenge(self):
        self.assertTrue(run(Burst(RIGHT_THEN_LEFT), steps=("turn_right", "turn_left"))["challenge_completed"])
        self.assertFalse(run(Burst(RIGHT_THEN_LEFT), steps=("turn_left", "turn_right"))["challenge_completed"])

    def test_direction_convention_the_nose_moving_toward_the_frame_right_is_the_persons_left(self):
        left_only = run(Burst([0.0, 0.0, 0.0, 0.0, 0.3, 0.35, 0.3, 0.0, 0.0, 0.0, 0.0]), steps=("turn_left",))
        self.assertTrue(left_only["challenge_completed"])
        right_only = run(Burst([0.0, 0.0, 0.0, 0.0, -0.3, -0.35, -0.3, 0.0, 0.0, 0.0, 0.0]), steps=("turn_left",))
        self.assertFalse(right_only["challenge_completed"])

    def test_shaking_the_head_both_ways_does_not_pass_a_single_step(self):
        r = run(Burst([0.0, 0.0, 0.0, -0.3, -0.35, -0.1, 0.0, 0.3, 0.35, 0.1, 0.0]), steps=("turn_left",))
        self.assertFalse(r["challenge_completed"])
        self.assertIn("WRONG_DIRECTION", r["issues"])

    def test_a_turn_that_is_too_small_does_not_count(self):
        r = run(Burst([0.0, 0.0, 0.0, 0.05, 0.1, 0.15, 0.1, 0.05, 0.0, 0.0, 0.0]), steps=("turn_left",))
        self.assertFalse(r["challenge_completed"])
        self.assertNotIn("WRONG_DIRECTION", r["issues"])

    def test_a_still_face_never_completes(self):
        r = run(Burst([0.0] * 12), steps=("turn_left",))
        self.assertFalse(r["challenge_completed"])
        self.assertEqual(r["steps"][0]["completed"], False)

    def test_the_same_direction_twice_needs_a_return_to_centre(self):
        no_return = run(Burst([0.0, 0.0, 0.0, 0.3, 0.35, 0.3, 0.32, 0.35, 0.3, 0.3, 0.3]), steps=("turn_left", "turn_left"))
        self.assertEqual([s["completed"] for s in no_return["steps"]], [True, False])
        with_return = run(Burst([0.0, 0.0, 0.0, 0.3, 0.35, 0.05, 0.0, 0.3, 0.38, 0.1, 0.0]), steps=("turn_left", "turn_left"))
        self.assertTrue(with_return["challenge_completed"])

    def test_a_missing_frame_in_the_middle_is_tolerated(self):
        yaws = list(LEFT_THEN_RIGHT)
        yaws[7] = None
        self.assertTrue(run(Burst(yaws))["challenge_completed"])

    def test_three_steps(self):
        yaws = [0, 0, 0, 0.3, 0.35, 0.0, -0.3, -0.35, 0.0, 0.3, 0.4, 0.0, 0.0]
        self.assertTrue(run(Burst(yaws), steps=("turn_left", "turn_right", "turn_left"))["challenge_completed"])


class NoHeadTurn(unittest.TestCase):
    def test_no_steps_needs_no_turn_and_checks_the_same_face_at_the_end(self):
        r = run(Burst([0.0, 0.02, 0.0, 0.01, 0.0, 0.02, 0.0]), steps=())
        self.assertTrue(r["challenge_completed"])
        self.assertEqual(r["issues"], [])
        self.assertEqual(r["continuity"]["checked"], 1)
        self.assertTrue(r["continuity"]["same_person"])

    def test_a_different_face_at_the_end_is_still_caught(self):
        vectors = iter([[1.0, 0.0, 0.0], [0.0, 1.0, 0.0]])
        r = run(Burst([0.0] * 7), steps=(), embed=lambda _i: next(vectors))
        self.assertIn("FACE_CHANGED", r["issues"])


class NeutralFrame(unittest.TestCase):
    def test_the_most_frontal_early_frame_is_chosen(self):
        r = run(Burst([0.08, 0.02, 0.1, 0.0, 0.3, 0.35, 0.05, -0.3, -0.35, 0.0, 0.0]))
        self.assertEqual(r["neutral_frame_index"], 3)

    def test_starting_already_turned_has_no_neutral_frame(self):
        r = run(Burst([0.3, 0.32, 0.3, 0.31, 0.3, 0.0, 0.0, 0.0, 0.0, 0.0]), steps=("turn_left",))
        self.assertIsNone(r["neutral_frame_index"])
        self.assertIn("NO_NEUTRAL_FRAME", r["issues"])
        self.assertFalse(r["challenge_completed"])
        self.assertIsNone(r["identification"])

    def test_the_neutral_frame_is_quality_checked_with_the_cached_detection(self):
        r = run(Burst(LEFT_THEN_RIGHT))
        self.assertIsNotNone(r["neutral_quality"])
        self.assertEqual(r["neutral_quality"]["face_count"], 1)
        self.assertEqual(r["neutral_quality"]["issues"], [])


class FrameChecks(unittest.TestCase):
    def test_losing_the_face_fails(self):
        yaws = [0.0, None, None, 0.0, None, 0.3, None, None, 0.3, None, 0.0, None]
        self.assertIn("FACE_LOST", run(Burst(yaws), steps=("turn_left",))["issues"])

    def test_non_increasing_timestamps_fail(self):
        burst = Burst(LEFT_THEN_RIGHT)
        burst.frames[5]["t_ms"] = burst.frames[4]["t_ms"]
        self.assertIn("BAD_TIMING", run(burst)["issues"])

    def test_a_burst_that_is_too_fast_fails(self):
        self.assertIn("BAD_TIMING", run(Burst(LEFT_THEN_RIGHT, step_ms=20))["issues"])

    def test_a_burst_that_is_too_slow_fails(self):
        self.assertIn("BAD_TIMING", run(Burst(LEFT_THEN_RIGHT, step_ms=3000))["issues"])

    def test_the_same_frame_repeated_is_flagged(self):
        same = frame_image(1)
        burst = Burst(LEFT_THEN_RIGHT, images=[same] * len(LEFT_THEN_RIGHT))
        r = run(burst)
        self.assertIn("DUPLICATE_FRAMES", r["issues"])
        self.assertGreater(r["duplicate_frame_frac"], 0.9)

    def test_several_comparable_faces_fail(self):
        burst = Burst(LEFT_THEN_RIGHT)
        original = burst.detector
        burst.detector = lambda img: original(img) + [{**face(0.0), "box": (200, 20, 130, 150)}]
        self.assertIn("MULTIPLE_FACES", run(burst)["issues"])

    def test_undecodable_frames_are_skipped_not_fatal(self):
        burst = Burst(LEFT_THEN_RIGHT)
        burst.frames[2]["image_base64"] = "not-an-image"
        burst.yaws.pop(2)  # the detector is never called for that frame
        r = run(burst)
        self.assertEqual(r["frames_usable"], len(LEFT_THEN_RIGHT) - 1)
        self.assertIsNone(r["yaw_series"][2])

    def test_input_limits(self):
        with self.assertRaises(ValueError):
            lv.analyze_liveness(["spin"], Burst(LEFT_THEN_RIGHT).frames)
        with self.assertRaises(ValueError):
            lv.analyze_liveness(["turn_left"] * 4, Burst(LEFT_THEN_RIGHT).frames)
        with self.assertRaises(ValueError):
            lv.analyze_liveness(["turn_left"], Burst([0.0] * 3).frames)
        with self.assertRaises(ValueError):
            lv.analyze_liveness(["turn_left"], Burst([0.0] * 17).frames)


class PassiveAntiSpoofing(unittest.TestCase):
    def test_scores_are_summarised(self):
        scores = iter([0.99, 0.9, 0.8, 0.95, 0.97])
        r = run(Burst(LEFT_THEN_RIGHT), pad=lambda _i, _b: next(scores))
        p = r["passive"]
        self.assertTrue(p["available"])
        self.assertEqual(p["frames_scored"], 5)
        self.assertEqual(p["min"], 0.8)
        self.assertEqual(p["real_frac"], 1.0)

    def test_spoof_scores_are_reported_as_such(self):
        r = run(Burst(LEFT_THEN_RIGHT), pad_value=0.03)
        self.assertEqual(r["passive"]["real_frac"], 0.0)
        self.assertLess(r["passive"]["mean"], 0.1)

    def test_a_missing_model_is_reported_never_silently_treated_as_real(self):
        r = run(Burst(LEFT_THEN_RIGHT), pad=lambda _i, _b: None)
        self.assertFalse(r["passive"]["available"])
        self.assertIn("PASSIVE_UNAVAILABLE", r["issues"])

    def test_the_neutral_frame_is_always_scored(self):
        seen = []
        run(Burst(LEFT_THEN_RIGHT), pad=lambda img, box: seen.append(int(img.sum())) or 0.9)
        neutral_sum = int(cv2.imdecode(np.frombuffer(base64.b64decode(Burst(LEFT_THEN_RIGHT).frames[2]["image_base64"]), np.uint8), 1).sum())
        self.assertIn(neutral_sum, seen)
        self.assertLessEqual(len(seen), lv.PAD_MAX_FRAMES)


class Continuity(unittest.TestCase):
    def test_the_same_face_throughout_is_continuous(self):
        r = run(Burst(LEFT_THEN_RIGHT), embed=lambda _img: [1.0, 0.1, 0.0])
        self.assertTrue(r["continuity"]["same_person"])
        self.assertEqual(r["continuity"]["checked"], 2)

    def test_a_different_face_in_the_turned_frames_is_caught(self):
        vectors = iter([[1.0, 0.0, 0.0], [0.0, 1.0, 0.0], [0.0, 0.0, 1.0]])
        r = run(Burst(LEFT_THEN_RIGHT), embed=lambda _img: next(vectors))
        self.assertFalse(r["continuity"]["same_person"])
        self.assertIn("FACE_CHANGED", r["issues"])

    def test_an_embedding_failure_fails_closed(self):
        def boom(_img):
            raise RuntimeError("model crashed")
        r = run(Burst(LEFT_THEN_RIGHT), embed=boom)
        self.assertIn("EMBEDDING_FAILED", r["issues"])
        self.assertIsNone(r["identification"])
        self.assertIsNone(r["continuity"]["same_person"])


class Identification(unittest.TestCase):
    def test_the_neutral_frame_is_scored_against_the_candidates(self):
        seen = {}

        def scorer(embedding, candidates):
            seen["embedding"], seen["candidates"] = embedding, candidates
            return {"threshold": 0.4, "model": "m", "results": [{"guardian_id": 7, "best_score": 0.9, "best_reference_id": 0,
                                                                   "scores": [], "unreadable_reference_ids": []}]}

        cands = [{"guardian_id": 7, "references": [{"reference_id": 0, "embedding_json": "[1,0,0]"}]}]
        r = run(Burst(LEFT_THEN_RIGHT), candidates=cands, scorer=scorer)
        self.assertEqual(seen["embedding"], [1.0, 0.0, 0.0])
        self.assertEqual(seen["candidates"], cands)
        self.assertEqual(r["identification"]["results"][0]["guardian_id"], 7)

    def test_without_candidates_nothing_is_scored(self):
        called = []
        r = run(Burst(LEFT_THEN_RIGHT), scorer=lambda *_: called.append(1) or {})
        self.assertIsNone(r["identification"])
        self.assertEqual(called, [])


class Route(unittest.TestCase):
    def test_the_response_model_accepts_a_full_result(self):
        from app.models.schemas import LivenessRequest, LivenessResponse
        from app.routes import liveness as route
        full = run(Burst(LEFT_THEN_RIGHT), candidates=[{"guardian_id": 7, "references": [{"reference_id": 0, "embedding_json": "[1]"}]}],
                   scorer=lambda e, c: {"threshold": 0.4, "model": "m", "results": [
                       {"guardian_id": 7, "best_score": 0.9, "best_reference_id": 0, "scores": [{"reference_id": 0, "score": 0.9}],
                        "unreadable_reference_ids": []}]})
        request = LivenessRequest(steps=["turn_left"], frames=[{"image_base64": "x", "t_ms": i} for i in range(6)])
        with mock.patch.object(route, "analyze_liveness", return_value=full):
            response = route.liveness(request)
        self.assertIsInstance(response, LivenessResponse)
        self.assertTrue(response.challenge_completed)
        self.assertEqual(response.identification.results[0].guardian_id, 7)
        self.assertEqual(response.neutral_quality.face_count, 1)

    def test_bad_input_is_a_422(self):
        from fastapi import HTTPException
        from app.models.schemas import LivenessRequest
        from app.routes import liveness as route
        request = LivenessRequest(steps=["turn_left"], frames=[{"image_base64": "x", "t_ms": 0}])
        with self.assertRaises(HTTPException) as caught:
            route.liveness(request)
        self.assertEqual(caught.exception.status_code, 422)

    def test_the_schema_rejects_unknown_steps(self):
        from app.models.schemas import LivenessRequest
        with self.assertRaises(ValueError):
            LivenessRequest(steps=["jump"], frames=[{"image_base64": "x", "t_ms": 0}])


if __name__ == "__main__":
    unittest.main()
