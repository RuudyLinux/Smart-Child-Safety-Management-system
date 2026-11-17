"""The service key is the only thing between the internet and the face engine once the service sits behind a tunnel."""
import os
import unittest
from unittest import mock

from fastapi import HTTPException

from app import security


def _env(**values):
    base = {k: v for k, v in os.environ.items() if k not in ("FACE_SERVICE_KEY", "FACE_SERVICE_REQUIRE_KEY")}
    base.update(values)
    return mock.patch.dict(os.environ, base, clear=True)


class VerifyInternalKey(unittest.TestCase):
    def test_correct_key_is_accepted(self):
        with _env(FACE_SERVICE_KEY="s3cret-key-value"):
            self.assertIsNone(security.verify_internal_key("s3cret-key-value"))

    def test_wrong_missing_or_empty_key_is_refused(self):
        with _env(FACE_SERVICE_KEY="s3cret-key-value"):
            for bad in ("wrong", "", None, "s3cret-key-valu", "s3cret-key-value "):
                with self.assertRaises(HTTPException) as ctx:
                    security.verify_internal_key(bad)
                self.assertEqual(ctx.exception.status_code, 401, repr(bad))

    def test_the_comparison_is_constant_time(self):
        with _env(FACE_SERVICE_KEY="s3cret-key-value"), mock.patch("app.security.hmac.compare_digest", return_value=True) as compare:
            security.verify_internal_key("anything")
        compare.assert_called_once()

    def test_without_a_key_configured_development_still_works(self):
        with _env():
            self.assertIsNone(security.verify_internal_key(None))

    def test_when_a_key_is_required_but_not_configured_every_request_is_refused(self):
        with _env(FACE_SERVICE_REQUIRE_KEY="true"):
            with self.assertRaises(HTTPException) as ctx:
                security.verify_internal_key(None)
            self.assertEqual(ctx.exception.status_code, 503)


class StartupCheck(unittest.TestCase):
    def test_a_required_key_that_is_missing_stops_startup(self):
        with _env(FACE_SERVICE_REQUIRE_KEY="1"):
            with self.assertRaises(RuntimeError):
                security.startup_check()

    def test_a_required_key_that_is_too_short_stops_startup(self):
        with _env(FACE_SERVICE_REQUIRE_KEY="1", FACE_SERVICE_KEY="short"):
            with self.assertRaises(RuntimeError):
                security.startup_check()

    def test_a_long_enough_key_passes(self):
        with _env(FACE_SERVICE_REQUIRE_KEY="1", FACE_SERVICE_KEY="k" * 32):
            security.startup_check()

    def test_the_error_never_contains_the_key(self):
        with _env(FACE_SERVICE_REQUIRE_KEY="1", FACE_SERVICE_KEY="tiny-secret"):
            with self.assertRaises(RuntimeError) as ctx:
                security.startup_check()
        self.assertNotIn("tiny-secret", str(ctx.exception))

    def test_nothing_required_nothing_checked(self):
        with _env():
            security.startup_check()


class CallerIsTrusted(unittest.TestCase):
    """/health shows model details only to a caller holding the key."""

    def test_trusted_only_with_the_right_key(self):
        with _env(FACE_SERVICE_KEY="k" * 32):
            self.assertTrue(security.caller_is_trusted("k" * 32))
            self.assertFalse(security.caller_is_trusted("x" * 32))
            self.assertFalse(security.caller_is_trusted(None))

    def test_without_a_key_configured_everyone_is_the_developer(self):
        with _env():
            self.assertTrue(security.caller_is_trusted(None))


if __name__ == "__main__":
    unittest.main()
