"""The only composition root: the one module that knows every layer."""

import json
import sys
from datetime import UTC, datetime
from urllib.parse import quote

import uvicorn
from aiogram import Bot
from aiogram.client.session.aiohttp import AiohttpSession
from aiogram.client.telegram import TelegramAPIServer
from fastapi import FastAPI

from stickersnap_bot.api.app import Polling, UpdateDelivery, Webhook, create_app
from stickersnap_bot.api.telegram import create_dispatcher
from stickersnap_bot.application.chat import BotChat, ChatHistory, Notifications
from stickersnap_bot.application.feedback import SendFeedback
from stickersnap_bot.application.init_data import InitDataVerifier, sign_init_data
from stickersnap_bot.application.linking import Linking, LinkSignals
from stickersnap_bot.application.media import MediaLinks
from stickersnap_bot.application.miniapp import MiniApp
from stickersnap_bot.application.sign_in import SignIn
from stickersnap_bot.infrastructure.database.migrate import migrate
from stickersnap_bot.infrastructure.database.unit_of_work import SqlUnitOfWork, create_engine
from stickersnap_bot.infrastructure.settings import Settings
from stickersnap_bot.infrastructure.telegram.app_links import BotAppLinks
from stickersnap_bot.infrastructure.telegram.chat_messages import BotChatMessages
from stickersnap_bot.infrastructure.telegram.feedback_inbox import TelegramFeedbackInbox
from stickersnap_bot.infrastructure.telegram.sticker_sets import BotStickerSets


def _now() -> datetime:
    return datetime.now(UTC)


def build_app(settings: Settings) -> FastAPI:
    session = (
        AiohttpSession(api=TelegramAPIServer.from_base(settings.telegram_api_url))
        if settings.telegram_api_url
        else AiohttpSession()
    )
    token = settings.bot_token.get_secret_value()
    bot = Bot(token, session=session)
    delivery: UpdateDelivery = Polling()
    if settings.bot_mode == "webhook":
        # Settings already refused webhook mode without them
        assert settings.webhook_base_url is not None
        assert settings.webhook_secret is not None
        delivery = Webhook(settings.webhook_base_url, settings.webhook_secret.get_secret_value())
    engine = create_engine(settings.database_path)

    def unit_of_work() -> SqlUnitOfWork:
        return SqlUnitOfWork(engine)

    sign_in = SignIn(
        unit_of_work, InitDataVerifier(token, max_age=settings.init_data_max_age), clock=_now
    )
    miniapp = MiniApp(unit_of_work, BotStickerSets(bot), MediaLinks(token), clock=_now)
    messages = BotChatMessages(bot)
    # one history: an answer in the chat ends the editing of the last notice
    history = ChatHistory()
    chat = BotChat(unit_of_work, messages, history, clock=_now, download_url=settings.download_url)
    notifications = Notifications(unit_of_work, messages, history, clock=_now)
    inbox = (
        None
        if settings.feedback_chat_id is None
        else TelegramFeedbackInbox(bot, settings.feedback_chat_id)
    )
    send_feedback = SendFeedback(unit_of_work, inbox, clock=_now)
    linking = Linking(unit_of_work, BotAppLinks(bot), notifications, LinkSignals(), clock=_now)
    return create_app(
        bot,
        create_dispatcher(chat),
        delivery,
        sign_in,
        miniapp,
        notifications,
        send_feedback,
        linking,
        closing=[engine.dispose],
    )


def main() -> None:
    settings = Settings()
    # before the app: the schema is ready when the first update or request arrives
    migrate(settings.database_path)
    uvicorn.run(build_app(settings), host=settings.host, port=settings.port)


def print_launch_data() -> None:
    """`stickersnap-bot-launch-data <user id> [first name]`: launch data signed with the configured
    token, to open the Mini App in a plain browser against this service: /?tma=<printed>.
    Printed URL-encoded, ready for the query string."""
    if len(sys.argv) < 2 or not sys.argv[1].isdigit():
        sys.exit("usage: stickersnap-bot-launch-data <telegram user id> [first name]")
    user = {"id": int(sys.argv[1]), "first_name": sys.argv[2] if len(sys.argv) > 2 else "Dev"}
    token = Settings().bot_token.get_secret_value()
    fields = {
        "user": json.dumps(user, ensure_ascii=False),
        "auth_date": str(int(_now().timestamp())),
    }
    print(quote(sign_init_data(token, fields), safe=""))


if __name__ == "__main__":
    main()
