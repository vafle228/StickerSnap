import pytest

from stickersnap_bot.application import chat_texts
from stickersnap_bot.application.chat_messages import OpenApp, OpenUrl, Screen
from stickersnap_bot.domain.devices import DeviceOs
from stickersnap_bot.domain.packs import PackKind

CATS = "cats_by_StickerSnapAppBot"


@pytest.mark.parametrize(
    ("added", "said"),
    [
        (1, "добавлен 1 стикер."),
        (3, "добавлено 3 стикера."),
        (5, "добавлено 5 стикеров."),
        (11, "добавлено 11 стикеров."),
        (12, "добавлено 12 стикеров."),
        (21, "добавлен 21 стикер."),
        (22, "добавлено 22 стикера."),
        (111, "добавлено 111 стикеров."),
    ],
)
def test_added_stickers_are_counted_in_russian(added: int, said: str) -> None:
    message = chat_texts.stickers_added(CATS, "Котики", PackKind.STICKERS, added, 15)

    assert f"В пак «Котики» {said} Теперь в нём 15 из 120." == message.text


def test_an_emoji_pack_counts_up_to_its_own_limit() -> None:
    message = chat_texts.stickers_added(CATS, "Эмодзи", PackKind.EMOJI, 3, 15)

    assert message.text.endswith("Теперь в нём 15 из 200.")


def test_a_new_pack_is_added_through_its_kinds_link() -> None:
    stickers = chat_texts.pack_created(CATS, "Котики", PackKind.STICKERS, 1)
    emoji = chat_texts.pack_created(CATS, "Котики", PackKind.EMOJI, 1)

    assert stickers.title == "Пак «Котики» создан"
    assert stickers.text.startswith("В нём первый стикер. Добавьте пак в Telegram")
    assert stickers.buttons == (
        OpenUrl("Добавить в Telegram", f"https://t.me/addstickers/{CATS}"),
        OpenApp("Открыть пак", pack=CATS),
    )
    assert emoji.buttons[0] == OpenUrl("Добавить в Telegram", f"https://t.me/addemoji/{CATS}")


def test_a_pack_created_with_several_stickers_counts_them() -> None:
    message = chat_texts.pack_created(CATS, "Котики", PackKind.STICKERS, 3)

    assert message.text.startswith("В нём 3 стикера. ")


def test_a_linked_computer_leads_to_the_devices() -> None:
    message = chat_texts.device_linked("DESKTOP-7F3K2Q", DeviceOs.WINDOWS)

    assert message.title == "Компьютер привязан"
    assert message.text.startswith("DESKTOP-7F3K2Q (Windows) теперь публикует стикеры")
    assert message.buttons == (OpenApp("Устройства", Screen.DEVICES),)


@pytest.mark.parametrize(
    ("button", "param"),
    [
        (OpenApp("Мои паки"), None),
        (OpenApp("Устройства", Screen.DEVICES), "devices"),
        (OpenApp("Написать отзыв", Screen.FEEDBACK), "feedback"),
        (OpenApp("Открыть пак", pack=CATS), f"pack_{CATS}"),
    ],
)
def test_buttons_open_the_mini_app_on_the_boards_screens(button: OpenApp, param: str) -> None:
    assert button.start_param == param


def test_the_welcome_opens_the_mini_app_and_the_downloads() -> None:
    message = chat_texts.welcome("https://example.com/download")

    assert message.text.startswith("Привет! Это StickerSnap.")
    assert message.buttons == (
        OpenApp("Открыть StickerSnap"),
        OpenUrl("Скачать для Windows и Linux", "https://example.com/download"),
    )


def test_help_has_three_buttons() -> None:
    message = chat_texts.help_message("https://example.com/download")

    assert [b.text for b in message.buttons] == [
        "Мои паки",
        "Написать отзыв",
        "Скачать для Windows и Linux",
    ]
