"""The bot's messages, word for word as canvas board 17 «Бот: чат» has them."""

from stickersnap_bot.application.chat_messages import ChatMessage, OpenApp, OpenUrl, Screen
from stickersnap_bot.domain.devices import DeviceOs
from stickersnap_bot.domain.packs import PackKind

_OPEN = OpenApp("Открыть StickerSnap")
_FEEDBACK = OpenApp("Написать отзыв", Screen.FEEDBACK)
_OS_NAMES = {DeviceOs.WINDOWS: "Windows", DeviceOs.MACOS: "macOS", DeviceOs.LINUX: "Linux"}


def _download(url: str) -> OpenUrl:
    return OpenUrl("Скачать для Windows и Linux", url)


def welcome(download_url: str) -> ChatMessage:
    return ChatMessage(
        "Привет! Это StickerSnap.\n\n"
        "Стикеры делаются в приложении на компьютере: обрезаете видео или гифку, и оно само "
        "подгоняет стикер под правила Telegram и публикует в ваш пак.\n\n"
        "Здесь живут ваши паки. Откройте их кнопкой ниже или кнопкой StickerSnap слева от поля "
        "ввода.",
        buttons=(_OPEN, _download(download_url)),
    )


def help_message(download_url: str) -> ChatMessage:
    return ChatMessage(
        "StickerSnap делает видеостикеры на компьютере и публикует их в ваши паки.\n\n"
        "Здесь, в Telegram: паки, отправка друзьям, удаление стикеров, привязанные компьютеры "
        "и отзыв.\n\n"
        "Как привязать компьютер: в приложении «Настройки» → «Публикация в Telegram» → "
        "«Привязать».",
        buttons=(OpenApp("Мои паки"), _FEEDBACK, _download(download_url)),
    )


def feedback() -> ChatMessage:
    # The board sends the button alone, but Telegram wants some text with it
    return ChatMessage("Об ошибке или идее напишите в форме.", buttons=(_FEEDBACK,))


def not_read() -> ChatMessage:
    """The answer to text, a sticker, a photo, a voice message."""
    return ChatMessage(
        "Я не читаю сообщения в чате. Об ошибке или идее напишите в форме: так мы увидим ваш "
        "текст и ответим.",
        buttons=(_FEEDBACK, _OPEN),
    )


def made_on_computer(download_url: str) -> ChatMessage:
    """The answer to a video, a GIF or a file."""
    return ChatMessage(
        "Стикеры из видео делаются в StickerSnap на компьютере. Здесь их не собрать.",
        buttons=(_download(download_url),),
    )


def device_linked(name: str, os: DeviceOs) -> ChatMessage:
    return ChatMessage(
        f"{name} ({_OS_NAMES[os]}) теперь публикует стикеры в ваши паки. Если это были не вы, "
        "отвяжите его.",
        title="Компьютер привязан",
        buttons=(OpenApp("Устройства", Screen.DEVICES),),
    )


def pack_created(name: str, title: str, kind: PackKind, count: int) -> ChatMessage:
    inside = "В нём первый стикер." if count == 1 else f"В нём {_stickers(count)}."
    add = "addemoji" if kind is PackKind.EMOJI else "addstickers"
    return ChatMessage(
        f"{inside} Добавьте пак в Telegram, чтобы отправлять стикеры в чаты.",
        title=f"Пак «{title}» создан",
        buttons=(
            OpenUrl("Добавить в Telegram", f"https://t.me/{add}/{name}"),
            OpenApp("Открыть пак", pack=name),
        ),
    )


def stickers_added(name: str, title: str, kind: PackKind, added: int, count: int) -> ChatMessage:
    verb = "добавлен" if _form(added) == 0 else "добавлено"
    return ChatMessage(
        f"В пак «{title}» {verb} {_stickers(added)}. Теперь в нём {count} из {kind.limit}.",
        buttons=(OpenApp("Открыть пак", pack=name),),
    )


def _form(n: int) -> int:
    """Russian plural: 0 for 1 стикер, 1 for 3 стикера, 2 for 5 стикеров."""
    if n % 10 == 1 and n % 100 != 11:
        return 0
    if 2 <= n % 10 <= 4 and not 12 <= n % 100 <= 14:
        return 1
    return 2


def _stickers(n: int) -> str:
    return f"{n} {('стикер', 'стикера', 'стикеров')[_form(n)]}"


# The bot's profile (board 17, section 5): the service sets it on every start, so it follows git
# Description: the empty chat's «Что умеет этот бот?», up to 512 characters
DESCRIPTION = (
    "StickerSnap делает видеостикеры для Telegram из видео и гифок на компьютере и сам публикует "
    "их в ваши паки. Здесь вы смотрите паки, отправляете их друзьям, привязываете компьютер и "
    "пишете нам об ошибках."
)
# About: the profile's line under the name, up to 120 characters
ABOUT = "Видеостикеры для Telegram из видео и гифок. Делаются на компьютере, живут здесь."
# The menu button left of the input field, opening the Mini App
MENU_BUTTON = "StickerSnap"
