/**
 * This phone's ticket key. Generated once, stored as a NON-EXTRACTABLE CryptoKey in IndexedDB,
 * so the private key can't be copied out of the app. The public key is sent when buying a
 * ticket and baked into the ticket certificate — the ticket only works on this phone.
 *
 * Native upgrade path: move to Android Keystore / iOS Secure Enclave via a Capacitor plugin.
 */
import { b64u } from "./encoding";

const DB = "metrorail-keys";
const STORE = "keys";
const ID = "ticket-device-key";

function idb(): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => {
    const req = indexedDB.open(DB, 1);
    req.onupgradeneeded = () => req.result.createObjectStore(STORE);
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

async function get(): Promise<CryptoKeyPair | undefined> {
  const db = await idb();
  return new Promise((resolve, reject) => {
    const r = db.transaction(STORE).objectStore(STORE).get(ID);
    r.onsuccess = () => resolve(r.result as CryptoKeyPair | undefined);
    r.onerror = () => reject(r.error);
  });
}

async function put(pair: CryptoKeyPair): Promise<void> {
  const db = await idb();
  return new Promise((resolve, reject) => {
    const tx = db.transaction(STORE, "readwrite");
    tx.objectStore(STORE).put(pair, ID);
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
  });
}

export async function deviceKeyPair(): Promise<CryptoKeyPair> {
  const existing = await get();
  if (existing) return existing;
  const pair = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, false, [
    "sign",
    "verify",
  ]);
  await put(pair);
  return pair;
}

export async function devicePublicKeyB64(): Promise<string> {
  const { publicKey } = await deviceKeyPair();
  return b64u(new Uint8Array(await crypto.subtle.exportKey("spki", publicKey)));
}
