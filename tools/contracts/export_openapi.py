"""Writes contracts/openapi/<service>.json from the services' own code (single source of truth).

    uv run python tools/contracts/export_openapi.py          # CI fails if the files change
"""

import importlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SERVICES = ["network", "tracking", "eta", "realtime", "identity", "trips", "notifications",
            "ticketing", "billing"]

for name in SERVICES:
    app = importlib.import_module(f"metrorail_{name}.main").build_app()
    spec = app.openapi()
    spec["servers"] = [{"url": f"/api/{name}", "description": "via the gateway"}]
    out = ROOT / "contracts" / "openapi" / f"{name}.json"
    out.write_text(json.dumps(spec, indent=2, sort_keys=True) + "\n")
    print(f"{out.relative_to(ROOT)}  ({len(spec['paths'])} paths)")
