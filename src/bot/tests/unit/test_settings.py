import pytest
from pydantic import ValidationError

from stickersnap_bot.infrastructure.settings import Settings


@pytest.fixture(autouse=True)
def _clean_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    for name in ("BOT_TOKEN", "BOT_MODE", "WEBHOOK_BASE_URL", "WEBHOOK_SECRET"):
        monkeypatch.delenv(name, raising=False)


def test_reads_token_from_environment_and_polls_by_default(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("BOT_TOKEN", "123:secret")

    settings = Settings()

    assert settings.bot_token.get_secret_value() == "123:secret"
    assert settings.bot_mode == "polling"


def test_token_is_hidden_in_repr(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("BOT_TOKEN", "123:secret")

    assert "123:secret" not in repr(Settings())


def test_webhook_mode_needs_address_and_secret(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("BOT_TOKEN", "123:secret")
    monkeypatch.setenv("BOT_MODE", "webhook")

    with pytest.raises(ValidationError, match="WEBHOOK_BASE_URL"):
        Settings()


def test_webhook_mode_refuses_the_empty_values_of_env_example(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    monkeypatch.setenv("BOT_TOKEN", "123:secret")
    monkeypatch.setenv("BOT_MODE", "webhook")
    monkeypatch.setenv("WEBHOOK_BASE_URL", "https://bot.example")
    monkeypatch.setenv("WEBHOOK_SECRET", "")

    with pytest.raises(ValidationError, match="WEBHOOK_SECRET"):
        Settings()
