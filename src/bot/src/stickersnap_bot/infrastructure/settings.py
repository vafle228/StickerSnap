from pathlib import Path
from typing import Literal, Self

from pydantic import SecretStr, model_validator
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
    # another Bot API server, for a fake one in tests; the real Telegram when not set
    telegram_api_url: str | None = None
    database_path: Path = Path("data/bot.sqlite3")
    host: str = "127.0.0.1"
    port: int = 8080

    @model_validator(mode="after")
    def _webhook_needs_address_and_secret(self) -> Self:
        if self.bot_mode == "webhook" and (
            not self.webhook_base_url or self.webhook_secret is None
        ):
            raise ValueError("BOT_MODE=webhook needs WEBHOOK_BASE_URL and WEBHOOK_SECRET")
        return self
