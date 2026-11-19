"""Ticket cryptography. Format spec: contracts/ticket-qr.md (the apps implement the same).

1. Ticket certificate (server, at purchase):
       cert = b64u(payload_json) + "." + b64u(Ed25519_sig(payload_json))
   payload: {v, tid, uid, prd, from, to, line, trips, vf, vt, dpk}
   `dpk` = the buying phone's P-256 public key (SPKI DER, b64u). Ties the ticket to that phone.

2. QR content (phone, every 30 s):
       inner = {c: cert, s: slot, n: nonce, k: trip_no_hint}, slot = unix_time // 30
       sig   = ECDSA-P256-SHA256 by the phone's private key (kept in the secure enclave/keystore)
       QR    = "MR1:" + b64u( seal_for_scanners( json({...inner, d: b64u(sig)}) ) )

3. Sealing (so only MetroRail scanners can read it):
       X25519 ephemeral key + scanner public key -> HKDF-SHA256 -> AES-256-GCM
       sealed = epk(32) || iv(12) || ciphertext+tag
"""

import base64
import json
import os
import time
from dataclasses import dataclass

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey, Ed25519PublicKey
from cryptography.hazmat.primitives.asymmetric.utils import encode_dss_signature
from cryptography.hazmat.primitives.asymmetric.x25519 import X25519PrivateKey, X25519PublicKey
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF

SLOT_SECONDS = 30
PREFIX = "MR1:"
HKDF_INFO = b"metrorail-ticket-qr-v1"


def b64u(b: bytes) -> str:
    return base64.urlsafe_b64encode(b).rstrip(b"=").decode()


def unb64u(s: str) -> bytes:
    return base64.urlsafe_b64decode(s + "=" * (-len(s) % 4))


def canonical(obj: dict) -> bytes:
    return json.dumps(obj, separators=(",", ":"), sort_keys=True).encode()


class TicketError(Exception):
    def __init__(self, reason: str):
        super().__init__(reason)
        self.reason = reason


# ---- keys ----------------------------------------------------------------------------------

@dataclass
class Keys:
    issuer: Ed25519PrivateKey
    scanner: X25519PrivateKey  # scanners hold this (Android Keystore); rotate per release

    @classmethod
    def load_or_generate(cls, issuer_pem: str | None, scanner_pem: str | None) -> "Keys":
        def load(pem, gen):
            return serialization.load_pem_private_key(pem.encode(), None) if pem else gen()
        return cls(load(issuer_pem, Ed25519PrivateKey.generate),
                   load(scanner_pem, X25519PrivateKey.generate))

    def public(self) -> dict:
        raw = serialization.Encoding.Raw, serialization.PublicFormat.Raw
        return {"issuer_ed25519": b64u(self.issuer.public_key().public_bytes(*raw)),
                "scanner_x25519": b64u(self.scanner.public_key().public_bytes(*raw))}


# ---- 1. certificate ------------------------------------------------------------------------

def issue_cert(issuer: Ed25519PrivateKey, payload: dict) -> str:
    body = canonical(payload)
    return b64u(body) + "." + b64u(issuer.sign(body))


def open_cert(issuer_pub: Ed25519PublicKey, cert: str) -> dict:
    try:
        body_b64, sig_b64 = cert.split(".")
        body = unb64u(body_b64)
        issuer_pub.verify(unb64u(sig_b64), body)
    except (ValueError, InvalidSignature) as e:
        raise TicketError("invalid_ticket") from e
    return json.loads(body)


# ---- 3. sealing ----------------------------------------------------------------------------

def _kdf(shared: bytes, epk: bytes) -> bytes:
    return HKDF(algorithm=hashes.SHA256(), length=32, salt=epk, info=HKDF_INFO).derive(shared)


def seal(scanner_pub: X25519PublicKey, plaintext: bytes) -> bytes:
    eph = X25519PrivateKey.generate()
    epk = eph.public_key().public_bytes(serialization.Encoding.Raw, serialization.PublicFormat.Raw)
    key = _kdf(eph.exchange(scanner_pub), epk)
    iv = os.urandom(12)
    return epk + iv + AESGCM(key).encrypt(iv, plaintext, PREFIX.encode())


def unseal(scanner: X25519PrivateKey, sealed: bytes) -> bytes:
    if len(sealed) < 32 + 12 + 16:
        raise TicketError("invalid_code")
    epk, iv, ct = sealed[:32], sealed[32:44], sealed[44:]
    key = _kdf(scanner.exchange(X25519PublicKey.from_public_bytes(epk)), epk)
    try:
        return AESGCM(key).decrypt(iv, ct, PREFIX.encode())
    except Exception as e:  # noqa: BLE001
        raise TicketError("invalid_code") from e


# ---- 2. QR on the phone (reference implementation; the app does this in TypeScript) --------

def device_keypair() -> tuple[ec.EllipticCurvePrivateKey, str]:
    k = ec.generate_private_key(ec.SECP256R1())
    spki = k.public_key().public_bytes(serialization.Encoding.DER,
                                       serialization.PublicFormat.SubjectPublicKeyInfo)
    return k, b64u(spki)


def build_qr(cert: str, device_key: ec.EllipticCurvePrivateKey, scanner_pub_b64: str,
             trip_hint: int = 0, now: float | None = None) -> str:
    inner = {"c": cert, "s": int((now or time.time()) // SLOT_SECONDS), "n": b64u(os.urandom(8)),
             "k": trip_hint}
    sig = device_key.sign(canonical(inner), ec.ECDSA(hashes.SHA256()))
    sealed = seal(X25519PublicKey.from_public_bytes(unb64u(scanner_pub_b64)),
                  canonical({**inner, "d": b64u(sig)}))
    return PREFIX + b64u(sealed)


@dataclass
class ScannedQr:
    cert: dict
    slot: int
    nonce: str


def read_qr(keys: Keys, qr: str, now: float | None = None, max_slot_skew: int = 1) -> ScannedQr:
    """Everything a scanner can check offline. Raises TicketError(reason)."""
    if not qr.startswith(PREFIX):
        raise TicketError("not_a_metrorail_code")
    try:
        inner = json.loads(unseal(keys.scanner, unb64u(qr[len(PREFIX):])))
    except (ValueError, json.JSONDecodeError) as e:
        raise TicketError("invalid_code") from e
    cert = open_cert(keys.issuer.public_key(), inner["c"])
    try:
        dpk = serialization.load_der_public_key(unb64u(cert["dpk"]))
        sig = unb64u(inner.pop("d"))
        if len(sig) == 64:  # WebCrypto/Android give raw r||s; cryptography expects DER
            sig = encode_dss_signature(int.from_bytes(sig[:32]), int.from_bytes(sig[32:]))
        dpk.verify(sig, canonical(inner), ec.ECDSA(hashes.SHA256()))
    except (InvalidSignature, ValueError, KeyError) as e:
        raise TicketError("not_this_phone") from e
    slot_now = int((now or time.time()) // SLOT_SECONDS)
    if abs(slot_now - inner["s"]) > max_slot_skew:
        raise TicketError("expired_code")
    return ScannedQr(cert=cert, slot=inner["s"], nonce=inner["n"])
