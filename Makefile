# MetroRail — common commands. Needs: .NET 10 SDK, Node 20+, Docker (for `make up`).
TOOLS = dotnet run --project tools/MetroRail.Tools --no-build --
.PHONY: setup build test contracts up down logs dev app sim smoke

setup:            ## restore everything
	dotnet restore
	cd apps/customer && npm install

build:
	dotnet build

test:             ## all service tests (48)
	dotnet test

contracts: build  ## regenerate contracts/openapi/*.json from the services
	$(TOOLS) openapi

up:               ## full stack in Docker (Postgres, NATS, Kong, Centrifugo, 9 services)
	cp -n infra/.env.example infra/.env || true
	docker compose -f infra/docker-compose.yml --env-file infra/.env up --build -d

down:
	docker compose -f infra/docker-compose.yml down

logs:
	docker compose -f infra/docker-compose.yml logs -f --tail=100

dev: build        ## all services WITHOUT Docker (SQLite + local nats-server), gateway on :8080
	DEMO_TIME=07:14 $(TOOLS) dev

app:              ## customer app on http://localhost:5173 (talks to the gateway on :8080)
	cd apps/customer && npm run dev

sim:              ## move train 3412 (try: make sim MODE=stopped)
	$(TOOLS) sim --mode $(or $(MODE),moving)

smoke:            ## end-to-end check against a running stack + app<->server QR check
	$(TOOLS) smoke
	$(TOOLS) qr-interop
