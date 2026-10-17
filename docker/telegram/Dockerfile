# The bot service (src/telegram/bot). Built by docker/telegram/compose.yaml with src/telegram/bot as the context.
FROM python:3.13-slim

COPY --from=ghcr.io/astral-sh/uv:0.11.7 /uv /bin/uv
ENV UV_COMPILE_BYTECODE=1 UV_LINK_MODE=copy UV_PYTHON_DOWNLOADS=never

WORKDIR /app
# Dependencies first, so a code change does not reinstall them
COPY pyproject.toml uv.lock .python-version ./
RUN uv sync --locked --no-dev --no-install-project
COPY src ./src
RUN uv sync --locked --no-dev

RUN useradd --system --uid 10001 bot && mkdir /data && chown bot /data
USER bot

ENV PATH="/app/.venv/bin:$PATH" HOST=0.0.0.0 PORT=8080 DATABASE_PATH=/data/bot.sqlite3
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s \
  CMD ["python", "-c", "import urllib.request; urllib.request.urlopen('http://127.0.0.1:8080/health')"]
CMD ["stickersnap-bot"]
