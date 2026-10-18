"""`Authorization: Bearer <secret>`: the poll token while linking, the device key after it."""

from typing import Annotated

from fastapi import Depends, Header

from stickersnap_bot.application.errors import UnauthorizedError


def bearer(authorization: Annotated[str | None, Header()] = None) -> str:
    """The secret after `Bearer`, empty without one."""
    scheme, _, secret = (authorization or "").partition(" ")
    return secret if scheme.lower() == "bearer" else ""


def _device_key(secret: Annotated[str, Depends(bearer)]) -> str:
    if not secret:
        raise UnauthorizedError("no Bearer device key")
    return secret


# a linked desktop's calls; the use case checks the key itself
type DeviceKey = Annotated[str, Depends(_device_key)]
