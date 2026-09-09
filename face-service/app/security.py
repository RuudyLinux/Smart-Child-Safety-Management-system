"""
Internal shared-secret check for this service's endpoints.

This service must never be exposed to the public internet — it is called only
by ChildGuard.Api on an internal network. As a defense-in-depth measure, when
FACE_SERVICE_KEY is set in the environment, every request must present the
same value in the X-Internal-Key header. When the env var is not set (local
dev without it configured), the check is skipped so development is not blocked —
mirroring how the rest of the project treats optional secrets.
"""
import os
from typing import Optional

from fastapi import Header, HTTPException

_HEADER_NAME = "X-Internal-Key"


def verify_internal_key(x_internal_key: Optional[str] = Header(default=None, alias=_HEADER_NAME)) -> None:
    expected = os.environ.get("FACE_SERVICE_KEY")
    if not expected:
        return  # no key configured — dev mode, matches ChildGuard.Api's own optional-secret pattern
    if x_internal_key != expected:
        raise HTTPException(status_code=401, detail="Missing or invalid X-Internal-Key.")
