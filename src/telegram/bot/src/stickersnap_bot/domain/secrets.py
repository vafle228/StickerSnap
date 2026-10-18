import hashlib


def hash_secret(secret: str) -> str:
    """The service keeps only this hash of a device key or a poll token, never the secret itself.

    The secrets are long random strings, so a plain SHA-256 is enough: there is nothing to guess.
    """
    return hashlib.sha256(secret.encode()).hexdigest()
