/**
 * Rotating ticket QR, built on the phone (spec: contracts/ticket-qr.md).
 *
 *   inner  = {c: cert, s: floor(now/30), n: random nonce, k: trip hint}
 *   d      = ECDSA-P256-SHA256(inner) with this phone's private key
 *   sealed = X25519(ephemeral, scannerPub) -> HKDF-SHA256(salt=epk) -> AES-256-GCM(aad "MR1:")
 *   QR     = "MR1:" + b64u(epk | iv | ciphertext+tag)
 *
 * Only MetroRail scanners can open it; a screenshot is useless after ~30-60 s, and each code
 * can be scanned once.
 */
import { x25519 } from "@noble/curves/ed25519";

import { b64u, canonical, unb64u, utf8 } from "./encoding";

export const SLOT_SECONDS = 30;
const PREFIX = "MR1:";
const INFO = utf8("metrorail-ticket-qr-v1");

export async function buildQr(opts: {
  cert: string;
  deviceKey: CryptoKey; // ECDSA P-256 private key (non-extractable)
  scannerPubB64: string; // GET /api/ticketing/keys -> scanner_x25519
  tripHint?: number;
  now?: number; // ms
}): Promise<string> {
  const subtle = globalThis.crypto.subtle;
  const nonce = globalThis.crypto.getRandomValues(new Uint8Array(8));
  const inner = {
    c: opts.cert,
    s: Math.floor((opts.now ?? Date.now()) / 1000 / SLOT_SECONDS),
    n: b64u(nonce),
    k: opts.tripHint ?? 0,
  };
  const sig = new Uint8Array(
    await subtle.sign({ name: "ECDSA", hash: "SHA-256" }, opts.deviceKey, utf8(canonical(inner))),
  ); // raw r||s (64 bytes) — the server accepts this form
  const plaintext = utf8(canonical({ ...inner, d: b64u(sig) }));

  const ephPriv = x25519.utils.randomPrivateKey();
  const epk = x25519.getPublicKey(ephPriv);
  const shared = x25519.getSharedSecret(ephPriv, unb64u(opts.scannerPubB64));
  const ikm = await subtle.importKey("raw", shared, "HKDF", false, ["deriveKey"]);
  const aes = await subtle.deriveKey(
    { name: "HKDF", hash: "SHA-256", salt: epk, info: INFO },
    ikm,
    { name: "AES-GCM", length: 256 },
    false,
    ["encrypt"],
  );
  const iv = globalThis.crypto.getRandomValues(new Uint8Array(12));
  const ct = new Uint8Array(
    await subtle.encrypt({ name: "AES-GCM", iv, additionalData: utf8(PREFIX) }, aes, plaintext),
  );
  const sealed = new Uint8Array(epk.length + iv.length + ct.length);
  sealed.set(epk, 0);
  sealed.set(iv, epk.length);
  sealed.set(ct, epk.length + iv.length);
  return PREFIX + b64u(sealed);
}
