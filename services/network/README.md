# network service

Network service: stations, lines, timetables and departure boards.

- Code: `metrorail_network/` · Tests: `tests/` (`uv run pytest services/network`)
- API docs when running: http://localhost:8080/api/network/docs · Contract: `contracts/openapi/network.json`
- Image: `docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=network .`
