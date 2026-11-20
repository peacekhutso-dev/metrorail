# Notifications service

Notifications service: in-app alerts for saved trips (delays, held/stopped trains). SMS/email plans later.

- Run alone: `dotnet run --project services/Notifications` · Tests: `dotnet test --filter NotificationsTests`
- API docs when running: http://localhost:8080/api/notifications/docs · Contract: `contracts/openapi/notifications.json`
- Image: `docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Notifications .`
