"""A Bot API that answers from canned responses, for the adapters over aiogram's Bot."""

import json
from collections.abc import AsyncGenerator
from typing import Any

from aiogram import Bot
from aiogram.client.session.base import BaseSession
from aiogram.methods import TelegramMethod
from aiogram.methods.base import TelegramType


def ok(result: Any) -> tuple[int, dict[str, Any]]:
    return 200, {"ok": True, "result": result}


def bad_request(description: str) -> tuple[int, dict[str, Any]]:
    return 400, {"ok": False, "error_code": 400, "description": f"Bad Request: {description}"}


class FakeBotApi(BaseSession):
    """Answers Bot API methods with canned responses, checked by aiogram as real ones are."""

    def __init__(self) -> None:
        super().__init__()
        self.answers: dict[str, tuple[int, dict[str, Any]]] = {}
        self.files: dict[str, bytes] = {}
        self.calls: list[str] = []
        # the methods themselves, to read what was sent
        self.methods: list[TelegramMethod[Any]] = []

    async def make_request(
        self,
        bot: Bot,
        method: TelegramMethod[TelegramType],
        timeout: int | None = None,  # noqa: ASYNC109
    ) -> TelegramType:
        name = type(method).__name__
        self.calls.append(name)
        self.methods.append(method)
        status, body = self.answers[name]
        response = self.check_response(bot, method, status, json.dumps(body))
        return response.result  # type: ignore[return-value]

    async def stream_content(
        self,
        url: str,
        headers: dict[str, Any] | None = None,
        timeout: int = 30,  # noqa: ASYNC109
        chunk_size: int = 65536,
        raise_for_status: bool = True,
    ) -> AsyncGenerator[bytes]:
        self.calls.append(url.rsplit("/", 1)[-1])
        yield self.files[url.rsplit("/", 1)[-1]]

    async def close(self) -> None:
        pass
