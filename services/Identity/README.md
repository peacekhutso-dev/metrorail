# Identity service

Identity service: passwordless login (6-digit code by SMS or email), ES256 tokens + JWKS, consents, POPIA export/delete.

- Run alone: `dotnet run --project services/Identity` · Tests: `dotnet test --filter IdentityTests`
- API docs when running: http://localhost:8080/api/identity/docs · Contract: `contracts/openapi/identity.json`
- Image: `docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Identity .`
