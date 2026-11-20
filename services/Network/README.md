# Network service

Network service: stations, lines, timetables and departure boards.

- Run alone: `dotnet run --project services/Network` · Tests: `dotnet test --filter NetworkTests`
- API docs when running: http://localhost:8080/api/network/docs · Contract: `contracts/openapi/network.json`
- Image: `docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Network .`
