from typing import Any

from aiogram import Bot
from aiogram.methods import SetChatMenuButton, SetMyDescription, SetMyShortDescription

from stickersnap_bot.application.chat_texts import ABOUT, DESCRIPTION, MENU_BUTTON
from stickersnap_bot.infrastructure.telegram.bot_profile import BotProfile
from tests.integration.telegram.bot_api import FakeBotApi, ok

MINIAPP = "https://bot.example.com/"
SETTERS = ("SetMyDescription", "SetMyShortDescription", "SetChatMenuButton")


def _profile(
    api: FakeBotApi,
    description: str = "",
    about: str = "",
    menu: dict[str, Any] | None = None,
) -> None:
    api.answers["GetMyDescription"] = ok({"description": description})
    api.answers["GetMyShortDescription"] = ok({"short_description": about})
    api.answers["GetChatMenuButton"] = ok(menu or {"type": "commands"})
    for setter in SETTERS:
        api.answers[setter] = ok(True)


def _bot(api: FakeBotApi) -> Bot:
    return Bot("42:token", session=api)


async def test_a_new_bot_gets_the_texts_of_board_17_and_the_mini_app_button() -> None:
    api = FakeBotApi()
    _profile(api)

    await BotProfile(_bot(api), MINIAPP).apply()

    sent = {type(method).__name__: method for method in api.methods}
    description = sent["SetMyDescription"]
    assert isinstance(description, SetMyDescription)
    assert description.description == DESCRIPTION
    about = sent["SetMyShortDescription"]
    assert isinstance(about, SetMyShortDescription)
    assert about.short_description == ABOUT
    menu = sent["SetChatMenuButton"]
    assert isinstance(menu, SetChatMenuButton)
    assert menu.menu_button is not None
    assert menu.menu_button.model_dump(exclude_none=True) == {
        "type": "web_app",
        "text": MENU_BUTTON,
        "web_app": {"url": MINIAPP},
    }


async def test_a_profile_already_in_place_is_only_read() -> None:
    # the setters are rate limited and the service restarts on every deploy
    api = FakeBotApi()
    _profile(
        api,
        DESCRIPTION,
        ABOUT,
        {"type": "web_app", "text": MENU_BUTTON, "web_app": {"url": MINIAPP}},
    )

    await BotProfile(_bot(api), MINIAPP).apply()

    assert not set(api.calls) & set(SETTERS)


async def test_a_moved_mini_app_gets_its_new_address() -> None:
    api = FakeBotApi()
    old = {"type": "web_app", "text": MENU_BUTTON, "web_app": {"url": "https://old.example.com/"}}
    _profile(api, DESCRIPTION, ABOUT, old)

    await BotProfile(_bot(api), MINIAPP).apply()

    assert api.calls[-1] == "SetChatMenuButton"


async def test_without_a_mini_app_address_the_menu_button_is_left_alone() -> None:
    # the development bot: its Mini App runs on localhost, Telegram opens only HTTPS
    api = FakeBotApi()
    _profile(api)

    await BotProfile(_bot(api), None).apply()

    assert "GetChatMenuButton" not in api.calls
    assert "SetChatMenuButton" not in api.calls


def test_the_texts_fit_telegrams_limits() -> None:
    assert len(DESCRIPTION) <= 512
    assert len(ABOUT) <= 120
