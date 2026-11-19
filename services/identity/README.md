# identity service

Identity service: passwordless login (6-digit code by SMS or email), tokens, consents, POPIA export/delete.

- Code: `metrorail_identity/` · Tests: `tests/` (`uv run pytest services/identity`)
- API docs when running: http://localhost:8080/api/identity/docs · Contract: `contracts/openapi/identity.json`
- Image: `docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=identity .`
