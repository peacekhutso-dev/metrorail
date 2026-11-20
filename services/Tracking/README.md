# Tracking service

Tracking service: live train positions, moving/held/stopped detection, delays, crowd reports.

- Run alone: `dotnet run --project services/Tracking` · Tests: `dotnet test --filter TrackingTests`
- API docs when running: http://localhost:8080/api/tracking/docs · Contract: `contracts/openapi/tracking.json`
- Image: `docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Tracking .`
