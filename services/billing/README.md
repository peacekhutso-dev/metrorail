# billing service

Billing service: card payments through Pay@ (card only), 3-D Secure, saved cards (tokens), receipts.

- Code: `metrorail_billing/` · Tests: `tests/` (`uv run pytest services/billing`)
- API docs when running: http://localhost:8080/api/billing/docs · Contract: `contracts/openapi/billing.json`
- Image: `docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=billing .`
