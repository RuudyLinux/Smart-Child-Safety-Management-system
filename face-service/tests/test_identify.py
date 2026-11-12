"""Identity scoring with the embedding step faked: these test the comparison logic, not ArcFace."""
import json
import unittest
from unittest import mock

from app.models.schemas import IdentifyRequest, RegisterReferenceRequest
from app.routes.identify import identify, register_ref
from app.services import face_engine as fe


def ref(reference_id, vector):
    return {"reference_id": reference_id, "embedding_json": json.dumps(vector)}


PROBE = [1.0, 0.0, 0.0, 0.0]


def patched(embedding=PROBE):
    return mock.patch.object(fe, "compute_embedding", return_value=embedding)


class Identify(unittest.TestCase):
    def test_best_reference_wins_per_guardian(self):
        candidates = [{"guardian_id": 7, "references": [ref(1, [0.0, 1.0, 0, 0]), ref(2, [0.9, 0.1, 0, 0]), ref(3, [0.5, 0.5, 0, 0])]}]
        with patched():
            result = fe.identify_face("img", candidates)
        r = result["results"][0]
        self.assertEqual(r["best_reference_id"], 2)
        self.assertGreater(r["best_score"], 0.99)
        self.assertEqual([s["reference_id"] for s in r["scores"]], [1, 2, 3])
        self.assertEqual(result["threshold"], fe.MATCH_THRESHOLD)

    def test_each_candidate_is_scored_independently(self):
        candidates = [
            {"guardian_id": 1, "references": [ref(10, [1, 0, 0, 0])]},
            {"guardian_id": 2, "references": [ref(20, [0, 1, 0, 0])]},
        ]
        with patched():
            results = fe.identify_face("img", candidates)["results"]
        self.assertAlmostEqual(results[0]["best_score"], 1.0)
        self.assertAlmostEqual(results[1]["best_score"], 0.0)

    def test_unreadable_references_are_reported_not_fatal(self):
        candidates = [{"guardian_id": 1, "references": [
            {"reference_id": 1, "embedding_json": "not json"},
            ref(2, [1, 0]),
            ref(3, [1, 0, 0, 0]),
            {"reference_id": 4, "embedding_json": "{\"a\": 1}"},
        ]}]
        with patched():
            r = fe.identify_face("img", candidates)["results"][0]
        self.assertEqual(sorted(r["unreadable_reference_ids"]), [1, 2, 4])
        self.assertEqual(r["best_reference_id"], 3)

    def test_guardian_with_no_usable_reference_has_no_score(self):
        with patched():
            r = fe.identify_face("img", [{"guardian_id": 1, "references": []}])["results"][0]
        self.assertIsNone(r["best_score"])
        self.assertIsNone(r["best_reference_id"])

    def test_route_returns_the_response_model(self):
        request = IdentifyRequest(image_base64="x", candidates=[{"guardian_id": 1, "references": [ref(1, [1, 0, 0, 0])]}])
        with patched():
            response = identify(request)
        self.assertEqual(response.results[0].guardian_id, 1)
        self.assertAlmostEqual(response.results[0].best_score, 1.0)

    def test_schema_limits_candidates_and_references(self):
        with self.assertRaises(ValueError):
            IdentifyRequest(image_base64="x", candidates=[])
        with self.assertRaises(ValueError):
            IdentifyRequest(image_base64="x", candidates=[{"guardian_id": 1, "references": [ref(i, [1]) for i in range(11)]}])

    def test_unreadable_image_is_a_422_with_the_reason(self):
        from fastapi import HTTPException
        request = IdentifyRequest(image_base64="x", candidates=[{"guardian_id": 1, "references": [ref(1, [1])]}])
        with mock.patch.object(fe, "compute_embedding", side_effect=fe.FaceEngineError("No face was found")):
            with self.assertRaises(HTTPException) as caught:
                identify(request)
        self.assertEqual(caught.exception.status_code, 422)
        self.assertIn("No face", caught.exception.detail)


class RegisterReference(unittest.TestCase):
    def test_new_face_is_not_a_duplicate(self):
        with patched():
            r = fe.register_reference("img", [ref(1, [0, 1, 0, 0])])
        self.assertIsNone(r["duplicate_of"])
        self.assertEqual(json.loads(r["embedding_json"]), PROBE)

    def test_near_identical_photo_is_flagged_as_duplicate_of_the_closest(self):
        with patched():
            r = fe.register_reference("img", [ref(1, [0.5, 0.5, 0, 0]), ref(2, [1.0, 0.01, 0, 0])])
        self.assertEqual(r["duplicate_of"], 2)
        self.assertGreater(r["best_existing_score"], fe.DUPLICATE_SIMILARITY)

    def test_response_carries_the_match_threshold_for_the_resemblance_rule(self):
        with patched():
            r = fe.register_reference("img", [ref(1, [1.0, 0, 0, 0])])
        self.assertEqual(r["threshold"], fe.MATCH_THRESHOLD)

    def test_first_reference_has_no_comparison(self):
        with patched():
            r = fe.register_reference("img", [])
        self.assertIsNone(r["duplicate_of"])
        self.assertIsNone(r["best_existing_score"])

    def test_registration_still_uses_the_strict_embedding_rules(self):
        with mock.patch.object(fe, "compute_embedding", return_value=PROBE) as embed:
            fe.register_reference("img", [])
        embed.assert_called_once_with("img", strict=True)

    def test_route_maps_strict_failures_to_422(self):
        from fastapi import HTTPException
        with mock.patch.object(fe, "compute_embedding", side_effect=fe.FaceEngineError("More than one face")):
            with self.assertRaises(HTTPException) as caught:
                register_ref(RegisterReferenceRequest(image_base64="x"))
        self.assertEqual(caught.exception.status_code, 422)


if __name__ == "__main__":
    unittest.main()
