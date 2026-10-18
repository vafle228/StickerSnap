"""What a use case refuses with; the API turns each into the contract's code and HTTP status."""


class ApplicationError(Exception):
    pass


class BadRequestError(ApplicationError):
    """The request breaks a rule of the contract its schema cannot say."""


class UnauthorizedError(ApplicationError):
    """The launch data is not signed with the bot's token or is too old; the device key is
    unknown or revoked."""


class PackNotFoundError(ApplicationError):
    """No such pack, or it belongs to someone else: the two look the same from outside."""


class StickerNotFoundError(ApplicationError):
    pass


class DeviceNotFoundError(ApplicationError):
    pass


class LinkNotFoundError(ApplicationError):
    """No such link code, someone else's poll token, or a code that ran out or was answered:
    the Mini App shows «Ссылка устарела» for all of them."""


class PackInTelegramError(ApplicationError):
    """Only a pack Telegram no longer knows may be forgotten."""


class RateLimitedError(ApplicationError):
    """Telegram, or the service's own limit, asks to wait; the caller waits as long as said."""

    def __init__(self, retry_after_sec: int) -> None:
        super().__init__(f"retry after {retry_after_sec} s")
        self.retry_after_sec = retry_after_sec


class TelegramUnavailableError(ApplicationError):
    """Telegram did not answer, or answered with an error the service cannot explain."""
