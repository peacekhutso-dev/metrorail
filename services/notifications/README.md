# notifications service

Notifications service: in-app alerts for saved trips (delays, held/stopped trains, leave-now). SMS/email plans later.

- Code: `metrorail_notifications/` · Tests: `tests/` (`uv run pytest services/notifications`)
- API docs when running: http://localhost:8080/api/notifications/docs · Contract: `contracts/openapi/notifications.json`
- Image: `docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=notifications .`
