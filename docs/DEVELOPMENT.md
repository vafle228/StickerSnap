# StickerSnap — запуск и проверки

Обязательные правила и процесс: [AGENTS.md](../AGENTS.md). Все команды выполняются
из корня репозитория, кроме явно обозначенных каталогов. Матрица определяет проверки
затронутых частей; результаты текущей задачи записываются в [TASKS.md](../TASKS.md).

## Окружение и запуск

Node 24 из [.nvmrc](../.nvmrc), .NET SDK 10, Python 3.13, uv (CI фиксирует 0.11.7).
`src/server/global.json` выбирает Microsoft.Testing.Platform, но не фиксирует SDK.
FFmpeg 9.0 и yt-dlp закреплены в `tools/fetch-ffmpeg.mjs` и `tools/fetch-ytdlp.mjs`.
На Windows/Linux бинарники проверяются по SHA-256; на macOS FFmpeg собирается из
закреплённых git commits FFmpeg/libvpx. Версии брать из manifests/lockfiles и скриптов.

Из корня репозитория:

```powershell
npm ci
npm run fetch:ffmpeg
npm run fetch:ytdlp
dotnet run --project src/server/StickerSnap.Api
# В отдельном терминале:
npm run dev
```

API — `127.0.0.1:5180`, Vite — `127.0.0.1:5173`, proxy `/api` и `/hubs`.
`npm run build` собирает production SPA в `src/server/StickerSnap.Api/wwwroot`.
`/?mock` работает без API; `build:dev` сохраняет dev-инструменты и не подходит для релиза.

| Сценарий | Команды / адрес |
| --- | --- |
| Electron demo | В двух терминалах: `npm run dev -w src/web -- --port 5176`, `npm run dev -w src/desktop` |
| Electron с настоящим сервером | Последовательно: `npm run build`, `npm run server -w src/desktop`, `npm run start -w src/desktop` |
| Mini App | `npm run dev:miniapp`; `127.0.0.1:5174`, proxy `/v1` → `127.0.0.1:8080`; demo: `/?mock` |
| Production Mini App | `npm run build:miniapp` → `src/telegram/miniapp/dist` |
| Bot в Docker | `docker compose -f docker/telegram/compose.yaml up --build` |
| Observability локально | `docker compose -f docker/telegram/observability/compose.dev.yaml up`; Grafana на `127.0.0.1:3000` |

Bot без Docker, из `src/telegram/bot`:

```powershell
uv sync --locked
uv run --env-file ../../../.env stickersnap-bot
# После изменения таблиц:
uv run alembic revision --autogenerate -m "describe the schema change"
```

Корневой `.env` создавать по [.env.example](../.env.example), не перезаписывая существующий.
Использовать dev bot. Python сам `.env` не читает: передавать `--env-file`.
Development — polling, production — webhook. SQLite задаётся `DATABASE_PATH`
(по умолчанию `data/bot.sqlite3`).

.NET: `appsettings*.json` и `StickerSnap__*`: `DataDir`, `ServiceUrl`, `FfmpegDir`,
`YtDlpDir`, `Token`, `Mode`, `ParentPid`. Desktop-данные: системный ApplicationData/StickerSnap
(Windows — `%APPDATA%/StickerSnap`, Linux — `~/.config/StickerSnap`);
тесты используют отдельный каталог. Не чистить реальные
проекты/базы ради теста. `device.json` содержит секрет устройства.

Electron передаёт разовый токен серверу на loopback; API использует Bearer,
media/SignalR также поддерживают `access_token`. Сохранять `contextIsolation`,
`sandbox`, отключённый `nodeIntegration` и узкий bridge `window.stickersnap`.
Mini App отправляет `tma <initData>`; bot проверяет подпись/срок и отдаёт медиа
по подписанным ссылкам без токена Telegram. Production-секреты не переносить в клиент.

## Импорт по ссылке и копирование паков

Локальный API распознаёт отдельные видео YouTube и Giphy; произвольные URL/плейлисты
не являются поддерживаемым источником. YouTube использует yt-dlp; Giphy — отдельный HTTP-адаптер:
MP4 для GIF, WEBP для sticker с прозрачностью. Probe Giphy уже скачивает файл в локальный cache,
поэтому probe нельзя считать операцией без загрузки. Параметры tracking/позиции YouTube
нормализуются в `Core/Links/VideoLink.cs`; адаптеры скрыты за `Application/Links/IVideoSites`.

`POST /api/v1/links/probe` получает сведения; `/api/v1/downloads` управляет загрузками.
`DownloadService` обрабатывает их последовательно отдельно от render jobs, проверяет место,
создаёт проект из upload и удаляет незавершённый upload при сбое/отмене. Прогресс приходит
снимками `downloadUpdated` до 10 раз/с, окончание — сразу; завершённые записи живут 10 минут.
Сохранять отмену дерева процесса yt-dlp и контрактные коды ошибок сайта/сети.

`POST /api/v1/packs/import` добавляет в реестр существующий пак бота.
`POST /api/v1/packs/copy` создаёт новый пак через облачный `POST /v1/packs/copies`:
чужой исходный пак не становится редактируемым. Bot service копирует по `file_id` с
сохранением порядка, типа пака, первого emoji и формата каждого стикера (`static`, `animated`,
`video`). Поэтому список/preview пака не должен считать каждый файл WEBM: копии содержат
также WEBP/TGS, хотя редактор создаёт только video. Сохранять проверку владельца нового пака
и раздельную семантику импорта/копирования. Детали — в YAML-контрактах и `PackService.CopyAsync`.

## Проверки

Проверять затронутые части. Для бага добавить воспроизводящий тест на нужном уровне;
не тестировать статическую разметку или копию реализации. Юнит-тесты проверяют правила
и команды, интеграционные — адаптеры/контракты, E2E — критические пользовательские пути.
Для документации достаточно проверки фактов, ссылок и команд, без полного рендерного набора.

| Изменение | Минимальный набор |
| --- | --- |
| DS / SPA | `npm run lint`, `npm run typecheck`, `npm test`, `npm run build`, `npm run check:prod-build`; визуальная проверка UI |
| Токены / DS HTML | `npm run check:tokens`, `npm run build:design-system`, `npm run build:design-system -- --check`; offline HTML в браузере |
| API | `npm run check:api`, `npm run typecheck:api`, контрактные тесты затронутых сервисов |
| Desktop | `npm run lint -w src/desktop`, `npm run typecheck -w src/desktop`, `npm test -w src/desktop`; для запуска/IPC/упаковки — smoke |
| Mini App | `npm run lint -w src/telegram/miniapp`, `npm run typecheck -w src/telegram/miniapp`, `npm test -w src/telegram/miniapp`, `npm run build:miniapp`, `npm run check:prod-build -- src/telegram/miniapp/dist` |
| .NET, из `src/server` | `dotnet build StickerSnap.slnx -c Release`, `dotnet test --solution StickerSnap.slnx --filter-not-trait Category=Slow` |
| Рендер / media / загрузки | Из `src/server`: `dotnet test --solution StickerSnap.slnx --filter-not-trait Category=Online` с FFmpeg и yt-dlp, включая Slow; реальный SPA E2E для затронутого пути |
| Python, из `src/telegram/bot` | `uv run ruff format --check .`, `uv run ruff check .`, `uv run mypy`, `uv run lint-imports`, `uv run pytest` |
| Форматирование TS/CSS/tools | `npm run format:check`; исправлять только относящиеся к задаче файлы |

**Корневые `lint`, `typecheck`, `test` охватывают только DS и web.** Desktop/Mini App
проверять отдельно. Перед E2E установить Chromium: из `src/web` — `npx playwright install chromium`.

- `npm run e2e` — Chromium + MSW, порт 5199 (`E2E_PORT`).
- `npm run fetch:ffmpeg`, `npm run build`, `npm run e2e:server` — production SPA,
  настоящий ASP.NET/FFmpeg: импорт → правка → точное превью → экспорт → сохранность проекта.
  Обязательно для затрагивающих этот путь интеграционных/API-изменений; mocks не заменяют его.
- `npm run e2e -w src/telegram/miniapp` — Chromium с demo-данными Mini App.
- `npm run e2e -w src/desktop` — настоящий Electron; нужны собранные SPA, оболочка и сервер.
  `STICKERSNAP_APP` выбирает упакованное приложение, иначе используется `electron .`.
  Smoke проверяет комплектные FFmpeg и yt-dlp; перед запуском скачать оба инструмента.

### Серверные Slow и Online

Быстрый набор исключает `Category=Slow`. Полный набор без внешних сайтов:
из `src/server` — `dotnet test --solution StickerSnap.slnx --filter-not-trait Category=Online`.
Он включает реальные FFmpeg и yt-dlp: `RealYtDlpTests` работают с локальным HTTP-сервером.
Перед ним из корня выполнить `npm run fetch:ffmpeg` и `npm run fetch:ytdlp`.

`OnlineSitesTests` имеют оба trait: `Slow` и `Online`, обращаются к настоящим YouTube/Giphy.
**Автоматического skip или env opt-in нет:** обычный `dotnet test` без фильтра запускает их.
Для согласованной сетевой проверки отдельно выполнить из `src/server`:
`dotnet test --solution StickerSnap.slnx --filter-trait Category=Online`.
Проверять их при обновлении yt-dlp/адаптеров сайтов; успех локального mock/Slow-набора
не подтверждает доступность внешнего сайта.

CI запускает уже собранный xUnit executable из
`src/server/StickerSnap.Tests/bin/Release/net10.0`:

```text
dotnet StickerSnap.Tests.dll -trait- "Category=Slow"
dotnet StickerSnap.Tests.dll -trait "Category=Slow" -trait- "Category=Online"
```

Online не входят в CI; ручной эквивалент для того же executable —
`dotnet StickerSnap.Tests.dll -trait "Category=Online"`.

Mini App E2E и real-server E2E делят порт **5198**: запускать последовательно
или менять `E2E_PORT` / `E2E_SERVER_PORT`. Две копии real-server suite не запускать
параллельно даже с разными портами: используется общий временный каталог.

.NET: xUnit v3/Microsoft.Testing.Platform, AwesomeAssertions, NSubstitute, FakeTimeProvider.
TS: Vitest, MSW, happy-dom только для нуждающихся в DOM тестов. Python: pytest, async fixtures.
Сторы сбрасывать, временем управлять, не использовать sleep вместо синхронизации.
Мок Telegram не доказывает реальную публикацию. Live-проверки — в согласованной тестовой среде;
отдельно сообщать, какие внешние интеграции не проверены.

## CI, упаковка и эксплуатация

[Pipeline](../.github/workflows/pipeline.yml) запускается на push, теги `v*` и вручную:
проверки → сборка → быстрые тесты → Slow/E2E → `Pipeline passed`.
Перед merge нужен успех для последнего commit. Настройки branch protection проверять
отдельно: комментарий workflow не доказывает их включение. Dependency audit сейчас
не блокирует gate; зелёный Pipeline не означает отсутствие advisories.

Локальная упаковка: `npm run package -w src/desktop`; `-- --dev` — отдельное dev-приложение,
`-- --installer` — NSIS на Windows, AppImage/deb на Linux, DMG/ZIP на macOS.
Скрипт собирает SPA/оболочку, публикует self-contained .NET, вкладывает pinned FFmpeg,
yt-dlp и их лицензии. Низкоуровневый `dist`
не заменяет полный `package`; production нельзя подменять `build:dev`.

[Build app](../.github/workflows/build-app.yml) собирает Windows/Linux x64 и macOS arm64/x64
из того же commit, на котором прошёл Pipeline, использует его артефакты и выполняет smoke
приложений на соответствующих runners. Runtime ID Mac — `osx-arm64`/`osx-x64`, не `mac-*`.
Каждый Mac собирает FFmpeg на своей архитектуре; кросс-сборка из Windows не поддержана.
Нужны Xcode Command Line Tools (git/make/компилятор), pkg-config и nasm для Intel;
рецепт — `tools/build-ffmpeg.mjs`, подготовка CI — `.github/actions/pinned-ffmpeg/action.yml`.
Установщики Mac сейчас подписаны ad hoc (`identity: "-"`), без Apple certificate/notarization,
`hardenedRuntime: false`; не описывать их как подписанный Apple выпуск.
[Deploy bot](../.github/workflows/deploy-bot.yml) запускается вручную с `main`, использует
прошедший Pipeline, собирает bot/web images, обновляет VPS и проверяет HTTPS `/health`.

**Релизы выполняет пользователь.** Агент не запускает release workflow, не создаёт
теги и GitHub Releases. Production-деплой и изменения VPS — только по отдельному прямому
запросу. Разрешение на PR/merge не включает эти действия.

Production: Caddy обслуживает HTTPS/Mini App и проксирует bot API; bot работает через webhook,
SQLite находится в volume; backup ежедневно хранит две недели копий.
Compose include требует версии 2.20+. Alloy/Prometheus/Loki/Tempo запускаются профилем
`telemetry` только при GitHub variable `TELEMETRY=on`; deploy также задаёт bot OTLP endpoint.
Иначе workflow не включает профиль и не задаёт endpoint; при пустом
`OTEL_EXPORTER_OTLP_ENDPOINT` экспорт выключен (учитывать и `.env` сервиса).
Текущий deploy при выключении флага **удаляет контейнеры
и volumes телеметрии вместе с накопленными метриками/логами/трейсами**; это не пауза сбора.
Не менять флаг как побочный эффект задачи. Desktop-сервер по-прежнему не экспортирует
телеметрию независимо от переменных; dev observability запускается отдельно.
GitHub secrets: `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `VPS_KNOWN_HOSTS`;
variables: `BOT_DOMAIN`, `TELEMETRY`.
Production `.env` находится на VPS и не поставляется из Git. `BOT_DOMAIN` также нужен
публикуемым установщикам для подключения к облачному сервису.
