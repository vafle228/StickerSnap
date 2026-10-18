"""What a use case refuses with; the API turns each into the contract's code and HTTP status."""


class ApplicationError(Exception):
    pass


class UnauthorizedError(ApplicationError):
    """The launch data is not signed with the bot's token, or it is too old."""


class PackNotFoundError(ApplicationError):
    """No such pack, or it belongs to someone else: the two look the same from outside."""


class StickerNotFoundError(ApplicationError):
    pass


class DeviceNotFoundError(ApplicationError):
    pass


class PackInTelegramError(ApplicationError):
    """Only a pack Telegram no longer knows may be forgotten."""


class RateLimitedError(ApplicationError):
    """Telegram asks to wait; the caller waits as long as Telegram said."""

    def __init__(self, retry_after_sec: int) -> None:
        super().__init__(f"retry after {retry_after_sec} s")
        self.retry_after_sec = retry_after_sec


class TelegramUnavailableError(ApplicationError):
    """Telegram did not answer, or answered with an error the service cannot explain."""
