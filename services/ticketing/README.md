# ticketing service

Ticketing service: products, purchases, signed tickets, sealed rotating QR, validation and trip counting.

- Code: `metrorail_ticketing/` · Tests: `tests/` (`uv run pytest services/ticketing`)
- API docs when running: http://localhost:8080/api/ticketing/docs · Contract: `contracts/openapi/ticketing.json`
- Image: `docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=ticketing .`
