from datetime import timedelta
from pathlib import Path
from typing import Literal, Self

from pydantic import SecretStr, field_validator, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    """The service configuration, read from environment variables (the .env file in development,
    env_file of docker compose on the VPS). The token never lives in git."""

    model_config = SettingsConfigDict(extra="ignore")

    bot_token: SecretStr
    # polling in development, webhook in production
    bot_mode: Literal["polling", "webhook"] = "polling"
    # public HTTPS address of the service; the webhook is {webhook_base_url}/telegram/webhook
    webhook_base_url: str | None = None
    # checked against the X-Telegram-Bot-Api-Secret-Token header of every webhook call
    webhook_secret: SecretStr | None = None
    # the Mini App's public address, opened by the menu button; the production Mini App is served
    # by the same host as the service (docker/telegram/Caddyfile). Not set: the menu button stays
    # the commands list, as the development bot's Mini App runs on localhost
    miniapp_url: str | None = None
    # another Bot API server, for a fake one in tests; the real Telegram when not set
    telegram_api_url: str | None = None
    # «Скачать для Windows и Linux» in the bot's messages
    download_url: str = "https://github.com/vafle228/StickerSnap/releases/latest"
    database_path: Path = Path("data/bot.sqlite3")
    # the feedback chat: your private chat with the bot or a closed group; feedback is only
    # stored while it is not set
    feedback_chat_id: int | None = None
    # how long the Mini App's launch data stays good; Telegram signs it when the Mini App opens
    init_data_max_age: timedelta = timedelta(days=1)
    host: str = "127.0.0.1"
    port: int = 8080
    # what reaches stdout (docker logs) and, with telemetry on, Loki
    log_level: Literal["DEBUG", "INFO", "WARNING", "ERROR"] = "INFO"
    # OpenTelemetry's OTLP/HTTP collector: traces, metrics and logs go there (Alloy beside the
    # service on the VPS, http://alloy:4318; docker/telegram/observability). Not set: nothing is
    # exported, as in development and tests. OTEL_SERVICE_NAME, OTEL_RESOURCE_ATTRIBUTES and the
    # other OTEL_* variables are read by the SDK itself
    otel_exporter_otlp_endpoint: str | None = None

    @field_validator(
        "feedback_chat_id", "miniapp_url", "otel_exporter_otlp_endpoint", mode="before"
    )
    @classmethod
    def _empty_is_none(cls, value: object) -> object:
        # .env.example leaves it empty
        return None if value == "" else value

    @model_validator(mode="after")
    def _webhook_needs_address_and_secret(self) -> Self:
        # .env.example leaves them empty, and an empty secret is no secret
        secret = self.webhook_secret.get_secret_value() if self.webhook_secret else ""
        if self.bot_mode == "webhook" and (not self.webhook_base_url or not secret):
            raise ValueError("BOT_MODE=webhook needs WEBHOOK_BASE_URL and WEBHOOK_SECRET")
        return self
