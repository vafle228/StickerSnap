"""`Authorization: tma <initData>`: the Telegram user behind a call from the Mini App.

A dependency of its own, for every route Telegram's launch data signs: the Mini App's API now,
the link confirmation (S4) and feedback (F1) next.
"""

from typing import Annotated, cast

from fastapi import Depends, Header, Request

from stickersnap_bot.application.errors import UnauthorizedError
from stickersnap_bot.application.sign_in import SignIn


def _sign_in(request: Request) -> SignIn:
    # create_app puts it on the app's state
    return cast(SignIn, request.app.state.sign_in)


async def _telegram_user(
    sign_in: Annotated[SignIn, Depends(_sign_in)],
    authorization: Annotated[str | None, Header()] = None,
) -> int:
    scheme, _, init_data = (authorization or "").partition(" ")
    if scheme.lower() != "tma" or not init_data:
        raise UnauthorizedError("no tma Authorization header")
    return await sign_in(init_data)


# The caller's Telegram user id; 401 `unauthorized` without valid launch data
type TelegramUserId = Annotated[int, Depends(_telegram_user)]
