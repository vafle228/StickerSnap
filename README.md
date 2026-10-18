<p align="center">
  <img src=".github/assets/icon-256.png" alt="StickerSnap icon" width="112" height="112">
</p>

<h1 align="center">StickerSnap</h1>

<p align="center">
  <b>Turn any video or GIF into a Telegram video sticker and publish it to your pack in one click.</b>
</p>

<p align="center">
  <img alt="Status" src="https://img.shields.io/badge/status-in%20development-b9a6ff">
  <img alt="Platforms" src="https://img.shields.io/badge/platforms-Windows%20%7C%20Linux-8fb8ff">
  <img alt="Electron" src="https://img.shields.io/badge/Electron-React-f7a8d8">
  <img alt=".NET" src="https://img.shields.io/badge/ASP.NET%20Core-local%20server-4a4ff0">
  <img alt="FFmpeg" src="https://img.shields.io/badge/FFmpeg-%E2%89%A5%209.0-ffb38a">
</p>

<p align="center">
  <img src=".github/assets/banner.png" alt="StickerSnap: turn any video or GIF into a Telegram video sticker" width="100%">
</p>

---

## What it does

Telegram video stickers have strict rules: WEBM with VP9, 512 px on the longest side, up to 3 seconds, up to 256 KB, no audio. Getting a clip to fit by hand means juggling FFmpeg flags and re-encoding until the file is small enough.

StickerSnap does that for you:

1. **Drop a clip.** MP4, MOV, WEBM, GIF and other formats FFmpeg understands.
2. **Trim and frame it.** Pick the best 3 seconds on the timeline, crop, scale and position it on the 512 px canvas.
3. **Get a valid sticker.** The app renders WEBM VP9 and tunes quality until the file fits the 256 KB limit.
4. **Publish to Telegram.** Create a new pack or add to an existing one through the shared bot [@StickerSnapAppBot](https://t.me/StickerSnapAppBot). No bot token or BotFather setup needed on your side.

<!-- SCREENSHOT: editor -->
<!-- <p align="center"><img src=".github/assets/screenshot-editor.png" alt="StickerSnap editor" width="85%"></p> -->

## Features

- Editor with a timeline, trim handles and live preview
- Automatic fit to Telegram limits (size, duration, resolution, codec)
- "My packs": see your packs, add stickers, pick emoji
- Projects saved as `.ssproj` files, so you can come back and re-export
- Light and dark themes, five accent colors, follows your system theme

## Download

> StickerSnap is in early development. Builds for Windows and Linux will appear on the [Releases](../../releases) page.

## Build from source

<!-- TODO: fill in once the repository has code -->

Requirements:

- Node.js LTS
- .NET SDK
- FFmpeg 9.0 or newer in `PATH`

```bash
git clone https://github.com/vafle228/StickerSnap.git
cd StickerSnap
# commands will be added with the first code
```

## How it works

```
Electron + React UI  ──HTTP──▶  ASP.NET Core local server  ──▶  FFmpeg (WEBM VP9 render)
                                         │
                                         └──▶  Telegram Bot API (@StickerSnapAppBot)
```

The project (clips, trims, scene layout) is a JSON file. The local server turns it into an FFmpeg command, renders the sticker, checks it against Telegram's limits and uploads it to your pack.

## Development

Requires Node.js 20 and the .NET 10 SDK.

```sh
npm install
npm run fetch:ffmpeg                              # the pinned FFmpeg into tools/ffmpeg/<rid>
dotnet run --project src/server/StickerSnap.Api   # API on http://127.0.0.1:5180
npm run dev                                       # SPA with HMR on http://127.0.0.1:5173, proxies /api and /hubs
```

`npm run build` puts the SPA into `src/server/StickerSnap.Api/wwwroot`, and the server then serves
it on its own port.
The design system is the workspace package `src/ds` (`@stickersnap/ds`), shared by the SPA and the
Telegram Mini App: tokens, fonts, global CSS and components, imported from `@stickersnap/ds`.
Design tokens come from `src/ds/src/tokens.json`; after changing it run `npm run gen:tokens`.
The dev server shows all the components in both themes and five accents at http://127.0.0.1:5173/?showcase.
Without the server, http://127.0.0.1:5173/?mock runs the SPA on demo data (MSW and a fake SignalR hub,
typed by the API contract). In dev builds a bar on top of the window switches between the real
server and the demo scenarios such as `?mock=offline`; production builds have neither.
The API contract is `src/contract/openapi.yaml`; after changing it run `npm run gen:api`.
Unit tests run with `npm test`.

### Server

The server is its own .NET solution, `src/server/StickerSnap.slnx`, in Clean Architecture:
`StickerSnap.Core` (the domain), `StickerSnap.Application` (use cases and the repository and
service interfaces they need), `StickerSnap.Infrastructure` (JSON-file storage, FFmpeg, the Bot
API) and `StickerSnap.Api` (the ASP.NET host). Dependencies point inwards only, and
`StickerSnap.Tests` checks that. Package versions live in `src/server/Directory.Packages.props`.

```sh
cd src/server
dotnet test --solution StickerSnap.slnx                                  # every server test
dotnet test --solution StickerSnap.slnx --filter-not-trait Category=Slow # the fast ones, as CI
```

`npm run fetch:ffmpeg` downloads the LGPL build of FFmpeg 9.0 with libvpx that the server renders
with, pinned by version and SHA-256 (`tools/fetch-ffmpeg.mjs`); the binaries are never committed.

### Desktop app

The Electron shell lives in `src/desktop` (TypeScript, Electron 44); its tooling runs on Node.js 24,
the current LTS that `.nvmrc` pins (CI takes the same file).
It starts the local server on 127.0.0.1 with a one-time token, shows the SPA the server serves and
gives it `window.stickersnap` for system dialogs, paths and the file manager.

```sh
npm run dev -w src/web -- --port 5176   # Vite for the window below
npm run dev -w src/desktop              # in another terminal: the window over Vite and the demo data, no server
npm run package -w src/desktop          # the production app, unpacked, in src/desktop/release
npm run package -w src/desktop -- --dev # «StickerSnap Dev» with the dev bar and demo scenarios, in release/dev
npm run package -w src/desktop -- --installer   # installers: NSIS on Windows, AppImage and deb on Linux
```

VS Code has the same four builds in Terminal → Run Task («Приложение: прод», «Приложение: dev»,
«Установщик: прод», «Установщик: dev»). `npm run package` builds the SPA (`npm run build`, or
`npm run build:dev` with `--dev`), publishes the self-contained server and runs electron-builder,
taking a Node 24 from npx when the installed one is older. The production SPA never carries the demo
data or the dev bar: `npm run check:prod-build` checks `src/server/StickerSnap.Api/wwwroot`, and CI runs it after
every build. CI packages only production: the installers for both systems come from the manual
workflow «Build app» (GitHub → Actions → Build app → Run workflow → pick a branch → Run, or a tag
`v*`); they are attached to the run under Artifacts. It builds only a commit Pipeline passed, and
takes the production SPA from that Pipeline run. To give the app to other people, run it on main with
«Publish release» ticked: it tags the commit `v<version>` and publishes a GitHub Release with both
installers on the repository's Releases page. The version is the one typed in, or the latest
release with its patch number raised; the build fails before packaging when that version is
already released. Each installer carries the pinned FFmpeg next to the
server (`npm run fetch:ffmpeg`, which `npm run package` runs too); the smoke test checks the app
renders with it and not with an FFmpeg on PATH.

The development window opens `http://127.0.0.1:5176/?mock=desktop`; F12 opens the developer tools
outside the installed app. `npm run start -w src/desktop` runs the shell from the repository over the
server in `server-dist` (`npm run server -w src/desktop`), or over `dotnet run` without it.

### Bot service

The Telegram bot and its HTTP API for the desktop app and the Mini App live in `src/telegram/bot`: Python 3.13
with uv, aiogram 3 and FastAPI in one process, in the same layers as the server
(`stickersnap_bot.domain`, `application`, `infrastructure`, `api`; `main.py` is the only composition
root). import-linter checks that dependencies point inwards (`[tool.importlinter]` in
`src/telegram/bot/pyproject.toml`).

The bot token and the other settings come from environment variables. Copy `.env.example` in the
repository root to `.env` and put the dev bot's token in it; `.env` is in `.gitignore`, so the token
never reaches git or the image.

```sh
cd src/telegram/bot
uv sync                                   # Python and the locked dependencies into src/telegram/bot/.venv
uv run --env-file ../../../.env stickersnap-bot   # polling, /health on http://127.0.0.1:8080
uv run pytest                             # tests, as CI
uv run ruff check . && uv run mypy && uv run lint-imports
uv run alembic revision --autogenerate -m "what changed"   # a migration after editing database/tables.py
```

The service keeps users, linked computers, link codes and its packs in SQLite (`DATABASE_PATH`,
`data/bot.sqlite3` by default) and brings the file to the latest Alembic migration itself on every
start, so a deploy needs no separate migration step.

The Mini App's API (`/v1/miniapp/...`, `src/contract/bot-service.yaml`) checks Telegram's signature on
each call (`TelegramUserId` in `api/telegram_user.py`, for any route Telegram signs), reads the user's
sets from the Bot API and serves sticker files through short-lived signed links, so the token never
reaches the browser. `INIT_DATA_MAX_AGE` (a day by default) is how long the launch data stays good.

The bot's chat asks nothing: `/start`, `/help` and `/feedback` answer with text and buttons, anything
else gets one phrase, and the bot writes by itself when a computer is linked, a pack is created or
stickers are added (`application/chat.py`, texts in `application/chat_texts.py`). Buttons open the
Mini App on a screen through `https://t.me/<bot>?startapp=<screen>` links, so they need the Main Mini
App switched on in @BotFather. The commands menu is set on every start; «Скачать» leads to
`DOWNLOAD_URL`, the GitHub releases page by default.

A desktop links itself without the chat (`application/linking.py`): `POST /v1/links` gives a code
for ten minutes, a `https://t.me/<bot>?startapp=link_<code>` link and a poll token; the link opens
the Mini App on «Привязать этот компьютер?», which confirms or declines through
`/v1/miniapp/links/{code}`; the desktop waits with `GET /v1/links/{code}?wait=25` and gets its
device key once, and from then on calls with `Bearer <device key>` (`GET` and `DELETE /v1/device`).
The service stores only hashes of keys and poll tokens. Linking the same installation again replaces
its device, whoever it belonged to.

`POST /v1/feedback` takes «Написать отзыв» from the Mini App (`tma` launch data), a linked computer
(`Bearer` device key) or a desktop without a link, stores it in the `feedback` table and forwards it
to the chat in `FEEDBACK_CHAT_ID` (your private chat with the bot, or a closed group with the bot in
it), the desktop's diagnostics as a file in reply. Without `FEEDBACK_CHAT_ID` feedback is only stored;
a message Telegram refused stays in the table with an empty `forwarded_at`.

`docker/telegram` holds the image and its compose file: `docker compose -f docker/telegram/compose.yaml up --build`
runs the service with `.env` from the repository root and keeps SQLite in a volume. On the VPS the
`.env` with the production token lies beside `compose.yaml` and is passed with `BOT_ENV_FILE=./.env`;
it is created there by hand or by the deploy workflow from a GitHub secret, never committed.
In production `BOT_MODE=webhook` with `WEBHOOK_BASE_URL` and `WEBHOOK_SECRET`.

### Telegram Mini App

`src/telegram/miniapp` is the Mini App of @StickerSnapAppBot: the packs the bot made for a Telegram
user, a sticker up close with «Удалить из пака», «Поделиться» and the linked computers. Same stack as
the SPA (Vite, React, strict TypeScript, Zustand, TanStack Query, CSS Modules, no router) and the same
design system, `@stickersnap/ds`, in Telegram's light or dark theme with our colours. It talks to the
bot service by `src/contract/bot-service.yaml` (after changing it run `npm run gen:api`) and signs each
request with Telegram's `initData` (`Authorization: tma …`).

```sh
npm run dev:miniapp      # http://127.0.0.1:5174, proxies /v1 to the bot service on :8080
npm run build:miniapp    # production build into src/telegram/miniapp/dist
npm test -w src/telegram/miniapp && npm run e2e -w src/telegram/miniapp
```

Outside Telegram, http://127.0.0.1:5174/?mock runs it on demo data (MSW by the contract) with a dev
bar that switches the scenario (`?mock=offline`, `?mock=no-packs`…) and Telegram's theme
(`&theme=dark`) and plays Telegram's header: «Закрыть» / «Назад» and «⋯ → Настройки».
`&startapp=pack-<name>` opens a pack the way a bot link does. Production builds have none of it.

`/?tma=<launch data>` runs the dev Mini App against the real bot service instead, behind the same dev
bar. `uv run --env-file ../../../.env stickersnap-bot-launch-data <your Telegram id>` in
`src/telegram/bot` prints the launch data, signed with the service's token and URL-encoded for the
query string.

## Roadmap

- [ ] Stage 0: proof of concept (render pipeline, Bot API upload)
- [ ] Editor and timeline
- [ ] Packs and publishing
- [ ] MVP release for Windows and Linux

## License

<!-- TODO: choose a license (MIT is a common default) -->

---

<p align="center">
  Not affiliated with Telegram. Telegram is a trademark of its respective owner.
</p>
