from aiogram.exceptions import TelegramAPIError, TelegramRetryAfter

from stickersnap_bot.application.errors import RateLimitedError, TelegramUnavailableError


def translate(error: TelegramAPIError) -> Exception:
    """A Bot API error no adapter explains itself: wait as Telegram said, or Telegram is down."""
    if isinstance(error, TelegramRetryAfter):
        return RateLimitedError(error.retry_after)
    return TelegramUnavailableError(str(error))
