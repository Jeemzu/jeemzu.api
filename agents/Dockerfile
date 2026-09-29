# ── Build stage ───────────────────────────────────────────────────────────────
FROM python:3.11-slim AS build
WORKDIR /app

COPY pyproject.toml .
COPY app ./app
RUN pip install --no-cache-dir --upgrade pip \
    && pip install --no-cache-dir .

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM python:3.11-slim
WORKDIR /app

COPY --from=build /usr/local/lib/python3.11/site-packages /usr/local/lib/python3.11/site-packages
COPY --from=build /usr/local/bin /usr/local/bin

COPY . .

ENV PYTHONUNBUFFERED=1
EXPOSE 8000

# Render injects PORT; the default keeps docker compose and plain docker run working.
CMD ["sh", "-c", "uvicorn main:app --host 0.0.0.0 --port ${PORT:-8000}"]
