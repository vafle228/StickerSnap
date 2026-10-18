class DomainError(Exception):
    """A domain rule refused the change; the application maps it to a contract code."""


class InvalidValueError(DomainError):
    """A value breaks a domain rule: an empty device name, a too long title, a foreign pack name."""


class LinkNotPendingError(DomainError):
    """The link was already confirmed, declined or claimed."""


class LinkExpiredError(DomainError):
    """The link code outlived its ten minutes."""


class LinkNotConfirmedError(DomainError):
    """The computer asks for its key before the user pressed «Привязать»."""
