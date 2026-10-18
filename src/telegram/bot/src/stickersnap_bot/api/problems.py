"""Errors as RFC 9457 Problem Details with the contract's `code` (src/contract/bot-service.yaml)."""

from typing import Any

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from stickersnap_bot.application.errors import (
    ApplicationError,
    BadRequestError,
    BotBlockedError,
    DeviceNotFoundError,
    LinkNotFoundError,
    PackFullError,
    PackInTelegramError,
    PackNameInvalidError,
    PackNameTakenError,
    PackNotFoundError,
    RateLimitedError,
    StickerNotFoundError,
    TelegramUnavailableError,
    UnauthorizedError,
    ValidationFailedError,
)

_CODES: dict[type[ApplicationError], tuple[str, int, str]] = {
    BadRequestError: ("bad_request", 400, "Bad request"),
    UnauthorizedError: ("unauthorized", 401, "Unauthorized"),
    BotBlockedError: ("bot_blocked", 403, "The bot cannot act for the user"),
    PackNotFoundError: ("pack_not_found", 404, "Pack not found"),
    StickerNotFoundError: ("sticker_not_found", 404, "Sticker not found"),
    DeviceNotFoundError: ("device_not_found", 404, "Device not found"),
    LinkNotFoundError: ("link_not_found", 404, "Link code not found"),
    PackInTelegramError: ("pack_in_telegram", 409, "The pack is still in Telegram"),
    PackNameTakenError: ("pack_name_taken", 409, "The pack name is taken"),
    PackFullError: ("pack_full", 409, "The pack is full"),
    PackNameInvalidError: ("pack_name_invalid", 422, "Not a pack name of the bot"),
    ValidationFailedError: ("validation_failed", 422, "Telegram would refuse the sticker"),
    RateLimitedError: ("rate_limited", 429, "Asked to wait"),
    TelegramUnavailableError: ("telegram_error", 502, "Telegram did not answer"),
}


def problem(
    code: str,
    status: int,
    title: str,
    detail: str | None = None,
    params: dict[str, Any] | None = None,
    headers: dict[str, str] | None = None,
) -> JSONResponse:
    body: dict[str, Any] = {
        "type": f"https://stickersnap.app/problems/{code}",
        "status": status,
        "title": title,
        "code": code,
    }
    # an optional field is absent, never null (contract, «Формат»)
    if detail:
        body["detail"] = detail
    if params:
        body["params"] = params
    return JSONResponse(
        body, status_code=status, headers=headers, media_type="application/problem+json"
    )


async def _application_error(_: Request, error: Exception) -> JSONResponse:
    assert isinstance(error, ApplicationError)
    code, status, title = _CODES.get(type(error), ("internal", 500, "Internal error"))
    if isinstance(error, RateLimitedError):
        seconds = error.retry_after_sec
        return problem(
            code,
            status,
            title,
            params={"retryAfterSec": seconds},
            headers={"Retry-After": str(seconds)},
        )
    return problem(code, status, title, detail=str(error) or None)


async def _bad_request(_: Request, error: Exception) -> JSONResponse:
    return problem("bad_request", 400, "Bad request", detail=str(error))


def add_problem_handlers(app: FastAPI) -> None:
    app.add_exception_handler(ApplicationError, _application_error)
    app.add_exception_handler(RequestValidationError, _bad_request)
