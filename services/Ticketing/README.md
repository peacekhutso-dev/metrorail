# Ticketing service

Ticketing service: products, purchases, signed tickets, sealed rotating QR, validation and trip counting.

- Run alone: `dotnet run --project services/Ticketing` · Tests: `dotnet test --filter TicketingTests`
- API docs when running: http://localhost:8080/api/ticketing/docs · Contract: `contracts/openapi/ticketing.json`
- Image: `docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Ticketing .`
