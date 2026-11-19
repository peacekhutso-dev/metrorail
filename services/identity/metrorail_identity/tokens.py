"""Ed25519 (EdDSA) access tokens + JWKS so every other service can verify them offline."""

import base64
import hashlib
import secrets
from datetime import UTC, datetime, timedelta

import jwt
from cryptography.hazmat.primitives import serialization
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey

ACCESS_TTL = timedelta(minutes=15)
REFRESH_TTL = timedelta(days=30)


class Signer:
    def __init__(self, private_pem: str | None, issuer: str, audience: str):
        if private_pem:
            key = serialization.load_pem_private_key(private_pem.encode(), password=None)
            assert isinstance(key, Ed25519PrivateKey), "IDENTITY_SIGNING_KEY must be Ed25519"
        else:
            key = Ed25519PrivateKey.generate()  # local/test only: new key each start
        self.key = key
        self.issuer, self.audience = issuer, audience
        raw = key.public_key().public_bytes(serialization.Encoding.Raw,
                                            serialization.PublicFormat.Raw)
        self.kid = hashlib.sha256(raw).hexdigest()[:16]
        self.public_pem = key.public_key().public_bytes(
            serialization.Encoding.PEM, serialization.PublicFormat.SubjectPublicKeyInfo).decode()
        self.jwk = {"kty": "OKP", "crv": "Ed25519", "use": "sig", "alg": "EdDSA", "kid": self.kid,
                    "x": base64.urlsafe_b64encode(raw).rstrip(b"=").decode()}

    def access_token(self, user_id: str, roles: list[str]) -> str:
        now = datetime.now(UTC)  # real clock: token expiry must never follow DEMO_TIME
        return jwt.encode(
            {"sub": user_id, "roles": roles, "iss": self.issuer, "aud": self.audience,
             "iat": int(now.timestamp()), "exp": int((now + ACCESS_TTL).timestamp())},
            self.key, algorithm="EdDSA", headers={"kid": self.kid},
        )


def new_refresh_token() -> tuple[str, str]:
    token = secrets.token_urlsafe(32)
    return token, hash_secret(token)


def hash_secret(value: str) -> str:
    return hashlib.sha256(value.encode()).hexdigest()
