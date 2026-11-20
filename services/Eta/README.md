# Eta service

ETA service: predicted arrival (with a likely range) for every remaining stop.

- Run alone: `dotnet run --project services/Eta` · Tests: `dotnet test --filter EtaTests`
- API docs when running: http://localhost:8080/api/eta/docs · Contract: `contracts/openapi/eta.json`
- Image: `docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Eta .`
