# MetroRail — common commands. Needs: uv (Python), Node 20+, Docker (for `make up`).
.PHONY: setup test lint fmt contracts up down logs dev app sim smoke

setup:            ## install everything
	uv sync
	cd apps/customer && npm install

test:             ## all service tests
	uv run pytest -q

lint:             ## lint Python + typecheck the app
	uv run ruff check .
	cd apps/customer && npx tsc -b

fmt:
	uv run ruff check . --fix
	uv run ruff format .

contracts:        ## regenerate contracts/openapi/*.json from the services
	uv run python tools/contracts/export_openapi.py

up:               ## full stack in Docker (Postgres, NATS, Kong, Centrifugo, 9 services)
	cp -n infra/.env.example infra/.env || true
	docker compose -f infra/docker-compose.yml --env-file infra/.env up --build -d

down:
	docker compose -f infra/docker-compose.yml down

logs:
	docker compose -f infra/docker-compose.yml logs -f --tail=100

dev:              ## all services WITHOUT Docker (SQLite + local nats-server), gateway on :8080
	DEMO_TIME=07:14 uv run python tools/dev/run_all.py

app:              ## customer app on http://localhost:5173 (talks to the gateway on :8080)
	cd apps/customer && npm run dev

sim:              ## move train 3412 (try: make sim MODE=stopped)
	uv run python tools/simulator/simulate.py --mode $(or $(MODE),moving)

smoke:            ## end-to-end check against a running stack
	uv run python tools/e2e/smoke.py
	uv run python tools/e2e/qr_interop.py
