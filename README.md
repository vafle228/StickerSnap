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
dotnet run --project src/server   # API on http://127.0.0.1:5180
npm run dev                       # SPA with HMR on http://127.0.0.1:5173, proxies /api and /hubs
```

`npm run build` puts the SPA into `src/server/wwwroot`, and the server then serves it on its own port.
Design tokens come from `src/web/src/ds/tokens.json`; after changing it run `npm run gen:tokens`.
The design system components live in `src/web/src/ds` (import them from `src/ds`); the dev server shows
all of them in both themes and five accents at http://127.0.0.1:5173/?showcase.
Without the server, http://127.0.0.1:5173/?mock runs the SPA on demo data (MSW and a fake SignalR hub,
typed by the API contract); a switch in the corner picks a scenario such as `?mock=offline`.
The API contract is `src/contract/openapi.yaml`; after changing it run `npm run gen:api`.
Unit tests run with `npm test`.

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
