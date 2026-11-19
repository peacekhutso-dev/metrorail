# tracking service

Tracking service: live train positions, crowd reports, moving/held/stopped detection, delays.

- Code: `metrorail_tracking/` · Tests: `tests/` (`uv run pytest services/tracking`)
- API docs when running: http://localhost:8080/api/tracking/docs · Contract: `contracts/openapi/tracking.json`
- Image: `docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=tracking .`
