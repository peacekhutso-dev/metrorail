"""Access-token verification shared by every service.

Tokens are short-lived JWTs signed by the identity service with Ed25519 (alg EdDSA).
Services verify with the identity public key (JWT_PUBLIC_KEY, PEM) or via JWKS_URL.

Local/test convenience: "Bearer dev:<user_id>" is accepted when ALLOW_DEV_TOKENS=true
(never enabled in production).
"""

from dataclasses import dataclass, field

import jwt
from fastapi import Depends, Request

from .errors import Forbidden, Unauthorized


@dataclass
class Principal:
    user_id: str
    roles: set[str] = field(default_factory=set)

    def require(self, role: str) -> None:
        if role not in self.roles:
            raise Forbidden(f"Requires role '{role}'", code="missing_role")


_jwks_clients: dict[str, jwt.PyJWKClient] = {}


def verify_token(token: str, settings) -> Principal:
    if settings.allow_dev_tokens and not settings.is_production and token.startswith("dev:"):
        parts = token[4:].split(":")
        roles = set(parts[1].split(",")) if len(parts) > 1 and parts[1] else {"commuter"}
        return Principal(user_id=parts[0], roles=roles)
    try:
        if settings.jwks_url:
            client = _jwks_clients.setdefault(settings.jwks_url, jwt.PyJWKClient(settings.jwks_url))
            key = client.get_signing_key_from_jwt(token).key
        elif settings.jwt_public_key:
            key = settings.jwt_public_key
        else:
            raise Unauthorized("Token verification is not configured", code="auth_not_configured")
        claims = jwt.decode(
            token, key, algorithms=["EdDSA"], audience=settings.jwt_audience,
            issuer=settings.jwt_issuer,
        )
    except jwt.PyJWTError as e:
        raise Unauthorized(f"Invalid token: {e}", code="invalid_token") from e
    return Principal(user_id=claims["sub"], roles=set(claims.get("roles", [])))


def _bearer(request: Request) -> str | None:
    h = request.headers.get("authorization", "")
    return h[7:] if h.lower().startswith("bearer ") else None


def current_user(request: Request) -> Principal:
    token = _bearer(request)
    if not token:
        raise Unauthorized("Log in to use this feature", code="login_required")
    return verify_token(token, request.app.state.settings)


def optional_user(request: Request) -> Principal | None:
    token = _bearer(request)
    return verify_token(token, request.app.state.settings) if token else None


def require_role(role: str):
    def _dep(user: Principal = Depends(current_user)) -> Principal:
        user.require(role)
        return user

    return _dep
