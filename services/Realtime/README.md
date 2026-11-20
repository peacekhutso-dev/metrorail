# Realtime service

Realtime service: forwards live train updates and alerts to phones via Centrifugo (SSE), issues connection tokens.

- Run alone: `dotnet run --project services/Realtime` · Tests: `dotnet test --filter RealtimeTests`
- API docs when running: http://localhost:8080/api/realtime/docs · Contract: `contracts/openapi/realtime.json`
- Image: `docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Realtime .`
