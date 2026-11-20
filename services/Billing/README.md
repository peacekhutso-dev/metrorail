# Billing service

Billing service: card payments through Pay@ (card only), 3-D Secure, saved card tokens, receipts.

- Run alone: `dotnet run --project services/Billing` · Tests: `dotnet test --filter BillingTests`
- API docs when running: http://localhost:8080/api/billing/docs · Contract: `contracts/openapi/billing.json`
- Image: `docker build -f infra/docker/service.Dockerfile --build-arg SERVICE=Billing .`
