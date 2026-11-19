"""Pay@ gateway adapter. Card payments only.

MetroRail never sees a full card number: the card form is Pay@'s hosted/secure form shown inside
the app, and we only keep Pay@'s card token + brand + last 4 digits (PCI DSS SAQ-A scope).

Two implementations behind one interface:
- MockPayAt  (PAYAT_MODE=mock, default outside production): deterministic test cards for the demo
      4000 0000 0000 0002  -> approved after 3-D Secure (OTP 123456)
      4000 0000 0000 0069  -> declined by bank
- PayAtClient (PAYAT_MODE=sandbox|live): real Pay@ API using PAYAT_* credentials.
      TODO(Peace): map these methods to the Pay@ sandbox endpoints from your merchant docs
      (create transaction / hosted card page, 3-D Secure redirect, status, refund) and confirm
      the webhook signature header + algorithm.
"""

import hashlib
import hmac
import uuid
from dataclasses import dataclass
from typing import Protocol


@dataclass
class GatewayResult:
    gateway_ref: str
    status: str  # requires_card | requires_3ds | succeeded | declined
    card_form_url: str | None = None
    three_ds_url: str | None = None
    card_token: str | None = None
    brand: str | None = None
    last4: str | None = None
    decline_reason: str | None = None


class Gateway(Protocol):
    async def create(self, reference: str, amount_cents: int, card_token: str | None) -> GatewayResult: ...
    async def submit_card(self, gateway_ref: str, number: str, expiry: str, cvv: str,
                          name: str, save: bool) -> GatewayResult: ...
    async def confirm_3ds(self, gateway_ref: str, otp: str) -> GatewayResult: ...
    def verify_webhook(self, body: bytes, signature: str) -> bool: ...


def brand_of(number: str) -> str:
    return "visa" if number.startswith("4") else "mastercard" if number[:1] in "25" else "card"


def luhn_ok(number: str) -> bool:
    digits = [int(d) for d in number][::-1]
    total = sum(d if i % 2 == 0 else (d * 2 - 9 if d * 2 > 9 else d * 2) for i, d in enumerate(digits))
    return len(number) in (15, 16, 19) and total % 10 == 0


class MockPayAt:
    """Behaves like a card gateway with 3-D Secure, without any network calls."""

    DECLINE = "4000000000000069"
    OTP = "123456"

    def __init__(self, webhook_secret: str = "mock-secret"):
        self.secret = webhook_secret.encode()
        self.pending: dict[str, dict] = {}

    async def create(self, reference, amount_cents, card_token):
        ref = "PAT-" + uuid.uuid4().hex[:12]
        self.pending[ref] = {"reference": reference, "amount": amount_cents, "token": card_token}
        if card_token:  # saved card -> straight to bank approval
            return GatewayResult(ref, "requires_3ds", three_ds_url=f"mock://3ds/{ref}")
        return GatewayResult(ref, "requires_card", card_form_url=f"mock://card/{ref}")

    async def submit_card(self, gateway_ref, number, expiry, cvv, name, save):
        number = number.replace(" ", "")
        if not luhn_ok(number):
            return GatewayResult(gateway_ref, "requires_card", decline_reason="invalid_card_number")
        mm, yy = expiry.split("/")
        if (2000 + int(yy), int(mm)) < (2026, 11):
            return GatewayResult(gateway_ref, "requires_card", decline_reason="card_expired")
        p = self.pending[gateway_ref]
        p["number"] = number
        p["token"] = ("tok_" + uuid.uuid4().hex[:16]) if save else None
        return GatewayResult(gateway_ref, "requires_3ds", three_ds_url=f"mock://3ds/{gateway_ref}",
                             card_token=p["token"], brand=brand_of(number), last4=number[-4:])

    async def confirm_3ds(self, gateway_ref, otp):
        p = self.pending.get(gateway_ref, {})
        if p.get("number") == self.DECLINE:
            return GatewayResult(gateway_ref, "declined", decline_reason="declined_by_bank")
        if otp != self.OTP:
            return GatewayResult(gateway_ref, "requires_3ds", decline_reason="wrong_otp")
        return GatewayResult(gateway_ref, "succeeded")

    def verify_webhook(self, body, signature):
        expected = hmac.new(self.secret, body, hashlib.sha256).hexdigest()
        return hmac.compare_digest(expected, signature)


class PayAtClient:
    """Real Pay@ integration (sandbox / live). See TODO in the module docstring."""

    def __init__(self, base_url: str, merchant_id: str, api_key: str, webhook_secret: str):
        import httpx

        self.http = httpx.AsyncClient(base_url=base_url, timeout=10,
                                      headers={"Authorization": f"Bearer {api_key}"})
        self.merchant_id = merchant_id
        self.secret = webhook_secret.encode()

    async def create(self, reference, amount_cents, card_token):
        raise NotImplementedError("Map to Pay@ 'create transaction' (hosted card page / token charge)")

    async def submit_card(self, *a, **k):
        raise NotImplementedError("With Pay@'s hosted form the card never reaches our API")

    async def confirm_3ds(self, gateway_ref, otp):
        raise NotImplementedError("Pay@ completes 3-D Secure; we learn the result via webhook")

    def verify_webhook(self, body, signature):
        expected = hmac.new(self.secret, body, hashlib.sha256).hexdigest()
        return hmac.compare_digest(expected, signature)
