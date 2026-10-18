"""Port to the bot's chat with a user: what the bot writes there and how it changes it.

In the chat the bot never asks anything: every message is text and buttons that lead into the
Mini App or out of Telegram (canvas board 17 «Бот: чат»).
"""

from dataclasses import dataclass
from enum import StrEnum
from typing import Protocol


class Screen(StrEnum):
    """Where a button opens the Mini App: the link's `startapp`, read by the Mini App."""

    DEVICES = "devices"
    FEEDBACK = "feedback"


@dataclass(frozen=True)
class OpenApp:
    """Opens the Mini App: on «Мои паки» without a screen, on a pack with `pack`."""

    text: str
    screen: Screen | None = None
    pack: str | None = None

    @property
    def start_param(self) -> str | None:
        if self.pack is not None:
            return f"pack_{self.pack}"
        return self.screen.value if self.screen else None


@dataclass(frozen=True)
class OpenUrl:
    """Opens a page outside the chat: the downloads, a sticker set to add."""

    text: str
    url: str


type Button = OpenApp | OpenUrl


@dataclass(frozen=True)
class ChatMessage:
    text: str
    # a bold first line, as the notifications have
    title: str | None = None
    # one button per row, as on the board
    buttons: tuple[Button, ...] = ()


class ChatMessages(Protocol):
    async def send(self, chat_id: int, message: ChatMessage) -> int | None:
        """The sent message's id, or None when Telegram does not let the bot write to the user
        (the bot was stopped, or the user never pressed «Начать»)."""
        ...

    async def edit(self, chat_id: int, message_id: int, message: ChatMessage) -> bool:
        """False when the message can no longer be edited (deleted, or too old)."""
        ...
