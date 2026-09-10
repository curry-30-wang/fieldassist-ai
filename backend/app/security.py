import base64
import binascii
import hashlib
import hmac
import secrets


PBKDF2_ALGORITHM = "pbkdf2_sha256"
PBKDF2_ITERATIONS = 600_000
SALT_BYTES = 16
DIGEST_BYTES = 32


def _base64url_without_padding(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


def _decode_base64url(value: str, expected_length: int) -> bytes:
    if not value or "=" in value:
        raise ValueError("padding is not allowed")
    padding = "=" * (-len(value) % 4)
    decoded = base64.b64decode(value + padding, altchars=b"-_", validate=True)
    if len(decoded) != expected_length:
        raise ValueError("unexpected decoded length")
    return decoded


def hash_password(password: str) -> str:
    salt = secrets.token_bytes(SALT_BYTES)
    digest = hashlib.pbkdf2_hmac(
        "sha256",
        password.encode("utf-8"),
        salt,
        PBKDF2_ITERATIONS,
        dklen=DIGEST_BYTES,
    )
    return "$".join(
        (
            PBKDF2_ALGORITHM,
            str(PBKDF2_ITERATIONS),
            _base64url_without_padding(salt),
            _base64url_without_padding(digest),
        )
    )


def verify_password(password: str, password_hash: str) -> bool:
    try:
        algorithm, iterations, salt_text, digest_text = password_hash.split("$")
        if algorithm != PBKDF2_ALGORITHM or iterations != str(PBKDF2_ITERATIONS):
            return False
        salt = _decode_base64url(salt_text, SALT_BYTES)
        expected_digest = _decode_base64url(digest_text, DIGEST_BYTES)
        actual_digest = hashlib.pbkdf2_hmac(
            "sha256",
            password.encode("utf-8"),
            salt,
            PBKDF2_ITERATIONS,
            dklen=DIGEST_BYTES,
        )
    except (AttributeError, ValueError, binascii.Error, UnicodeEncodeError):
        return False
    return hmac.compare_digest(actual_digest, expected_digest)
