# Trips service

Trips service: a commuter's saved trips (name, stations, days, usual train, alert settings).

- Run alone: `dotnet run --project services/Trips` · Tests: `dotnet test --filter TripsTests`
- API docs when running: http://localhost:8080/api/trips/docs · Contract: `contracts/openapi/trips.json`
- Image: `docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Trips .`
