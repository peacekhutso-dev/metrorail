"""Checks that a QR built by the customer app (TypeScript) is accepted by ticketing (Python).

    uv run python tools/e2e/qr_interop.py
"""

import json
import subprocess
import sys
from pathlib import Path

from metrorail_ticketing.crypto import Keys, TicketError, issue_cert, read_qr

APP = Path(__file__).resolve().parents[2] / "apps" / "customer"


def tsx(*args: str) -> dict:
    out = subprocess.run(["npx", "tsx", "scripts/qr-interop.ts", *args], cwd=APP,
                         capture_output=True, text=True, check=True).stdout
    return json.loads(out.strip().splitlines()[-1])


def main() -> None:
    keys = Keys.load_or_generate(None, None)
    phone = tsx()
    cert = issue_cert(keys.issuer, {"v": 1, "tid": "MR-48213", "uid": "u1", "prd": "ten_trip",
                                    "from": "ST-RONDEBOSCH", "to": "ST-CLAREMONT",
                                    "line": "LN-SOUTHERN", "trips": 10, "vf": 0, "vt": 4102444800,
                                    "dpk": phone["dpk"]})
    qr = tsx(cert, keys.public()["scanner_x25519"], phone["pkcs8"])["qr"]
    scanned = read_qr(keys, qr)
    assert scanned.cert["tid"] == "MR-48213"
    print(f"✓ app QR ({len(qr)} chars) opened and verified by ticketing: {scanned.cert['tid']}")
    other = tsx()  # a different phone signing someone else's ticket must fail
    try:
        read_qr(keys, tsx(cert, keys.public()["scanner_x25519"], other["pkcs8"])["qr"])
        sys.exit("✗ QR from another phone was accepted")
    except TicketError as e:
        assert e.reason == "not_this_phone"
        print("✓ same ticket signed by another phone -> rejected (not_this_phone)")


if __name__ == "__main__":
    main()
