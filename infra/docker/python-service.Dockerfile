# One image recipe for every Python service. Build context = repo root.
#   docker build -f infra/docker/python-service.Dockerfile --build-arg SERVICE=network .
FROM python:3.12-slim AS base
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1 UV_SYSTEM_PYTHON=1
RUN pip install --no-cache-dir uv==0.5.11 && useradd --create-home --uid 10001 app
WORKDIR /srv

ARG SERVICE
COPY libs/py-common /srv/libs/py-common
COPY services/${SERVICE} /srv/services/${SERVICE}
RUN uv pip install --no-cache /srv/libs/py-common /srv/services/${SERVICE}

ENV SERVICE=${SERVICE}
USER app
EXPOSE 8000
HEALTHCHECK --interval=15s --timeout=3s --retries=5 \
  CMD python -c "import urllib.request,sys; urllib.request.urlopen('http://127.0.0.1:8000/health'); sys.exit(0)"
CMD ["sh", "-c", "exec uvicorn metrorail_${SERVICE}.main:app --host 0.0.0.0 --port 8000 --proxy-headers"]
