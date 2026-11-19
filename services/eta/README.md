# eta service

ETA service: predicted arrival times (with a likely range) for every remaining stop.

- Code: `metrorail_eta/` · Tests: `tests/` (`uv run pytest services/eta`)
- API docs when running: http://localhost:8080/api/eta/docs · Contract: `contracts/openapi/eta.json`
- Image: `docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=eta .`
