# realtime service

Realtime service: forwards live train updates and alerts to phones via Centrifugo (SSE), issues connection tokens.

- Code: `metrorail_realtime/` · Tests: `tests/` (`uv run pytest services/realtime`)
- API docs when running: http://localhost:8080/api/realtime/docs · Contract: `contracts/openapi/realtime.json`
- Image: `docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=realtime .`
