"""
Shared-secret check for this service's endpoints.

The service holds face-recognition capability and handles face images, so it must only ever be
called by ChildGuard.Api. Two deployment shapes exist:

* Private network (preferred): not reachable from the internet at all.
* Behind a tunnel (e.g. Tailscale Funnel on the school's own machine): the URL is public, so the key
  is the only gate. Set FACE_SERVICE_REQUIRE_KEY=true there: the service then refuses to start
  without a strong FACE_SERVICE_KEY, and answers 503 (never "open") if the key is somehow missing.

When FACE_SERVICE_KEY is set, every request must present the same value in the X-Internal-Key
header, compared in constant time. With neither variable set (local development) the check is
skipped, mirroring how the rest of the project treats optional secrets.
"""
import hmac
import os
from typing import Optional

from fastapi import Header, HTTPException

_HEADER_NAME = "X-Internal-Key"
MIN_KEY_LENGTH = 32


def _required() -> bool:
    return os.environ.get("FACE_SERVICE_REQUIRE_KEY", "").strip().lower() in ("1", "true", "yes")


def _matches(expected: str, presented: Optional[str]) -> bool:
    return bool(presented) and hmac.compare_digest(presented.encode("utf-8"), expected.encode("utf-8"))


def startup_check() -> None:
    """Stops the service from starting in a mode that would leave it open. Never includes the key in the message."""
    if not _required():
        return
    key = os.environ.get("FACE_SERVICE_KEY", "")
    if not key:
        raise RuntimeError("FACE_SERVICE_REQUIRE_KEY is set but FACE_SERVICE_KEY is not.")
    if len(key) < MIN_KEY_LENGTH:
        raise RuntimeError(f"FACE_SERVICE_KEY must be at least {MIN_KEY_LENGTH} characters.")


def caller_is_trusted(x_internal_key: Optional[str]) -> bool:
    """True when the caller may see service details (/health): it holds the key, or no key is configured (dev)."""
    expected = os.environ.get("FACE_SERVICE_KEY")
    if not expected:
        return not _required()
    return _matches(expected, x_internal_key)


def verify_internal_key(x_internal_key: Optional[str] = Header(default=None, alias=_HEADER_NAME)) -> None:
    expected = os.environ.get("FACE_SERVICE_KEY")
    if not expected:
        if _required():
            raise HTTPException(status_code=503, detail="Service key is not configured.")
        return  # no key configured — dev mode, matches ChildGuard.Api's own optional-secret pattern
    if not _matches(expected, x_internal_key):
        raise HTTPException(status_code=401, detail="Missing or invalid X-Internal-Key.")
