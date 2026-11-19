# trips service

Trips service: a commuter's saved trips (name, stations, days, usual train, alert settings).

- Code: `metrorail_trips/` · Tests: `tests/` (`uv run pytest services/trips`)
- API docs when running: http://localhost:8080/api/trips/docs · Contract: `contracts/openapi/trips.json`
- Image: `docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=trips .`
