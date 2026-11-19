/**
 * Proves the app's QR code is readable by the Python ticketing service.
 * Driven by tools/e2e/qr_interop.py:
 *   tsx scripts/qr-interop.ts                         -> {dpk, pkcs8}   (new phone key)
 *   tsx scripts/qr-interop.ts <cert> <scannerPub> <pkcs8>  -> {qr}
 */
import { b64u, unb64u } from "../src/ticket/encoding";
import { buildQr } from "../src/ticket/qr";

const [cert, scannerPub, pkcs8] = process.argv.slice(2);
const alg = { name: "ECDSA", namedCurve: "P-256" } as const;
if (!cert) {
  const pair = await crypto.subtle.generateKey(alg, true, ["sign", "verify"]);
  const spki = new Uint8Array(await crypto.subtle.exportKey("spki", pair.publicKey));
  const priv = new Uint8Array(await crypto.subtle.exportKey("pkcs8", pair.privateKey));
  console.log(JSON.stringify({ dpk: b64u(spki), pkcs8: b64u(priv) }));
} else {
  const key = await crypto.subtle.importKey("pkcs8", unb64u(pkcs8), alg, false, ["sign"]);
  console.log(JSON.stringify({ qr: await buildQr({ cert, deviceKey: key, scannerPubB64: scannerPub }) }));
}
