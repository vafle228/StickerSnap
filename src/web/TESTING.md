# Тесты SPA

Как мы пишем тесты в `src/web` и что ими проверяем. Общий процесс разработки — в
[AGENTS.md](../../AGENTS.md), текущие задачи — в [TASKS.md](../../TASKS.md), визуальный справочник —
[дизайн-система](../../docs/design-system.html).

## Запуск

```bash
npm test                  # юнит-, контрактные и компонентные тесты, из корня репозитория
npm test -w src/web -- timeline # только тесты SPA, в пути которых есть «timeline»
cd src/web && npx vitest  # режим наблюдения
npm run e2e               # e2e в Chromium; первый раз: cd src/web && npx playwright install chromium
npm run build && npm run e2e:server   # e2e на настоящем сервере с FFmpeg (нужен npm run fetch:ffmpeg)
cd src/server && dotnet test --solution StickerSnap.slnx --filter-not-trait Category=Online # сервер без реальных внешних сайтов
```

CI (`.github/workflows/pipeline.yml`) идёт на каждый push: проверки → сборка → быстрые тесты
(дизайн-система, SPA, оболочка, сервер, Mini App, бот) → e2e на демо-данных, e2e на настоящем
сервере и медленные тесты сервера. E2E ждут быстрые тесты соответствующей части проекта;
при падении к запуску прикладывается отчёт Playwright с трейсом. Установщики собирает отдельный ручной workflow
«Build app» (`.github/workflows/build-app.yml`): Actions → Build app → Run workflow. Он собирает
только коммит, на котором Pipeline прошёл, и берёт из этого прогона собранный SPA.

## Что тестируем

- **Логику, а не разметку.** Математика таймлайна, кропа и кадра, сторы, команды редактора,
  форматирование, оповещения. Мы держим её вне компонентов, в `model/` и командах, и проверяем без DOM.
- **Команды, а не кнопки.** Горячая клавиша и кнопка вызывают одну и ту же команду
  (`markEdge`, `addSegment`, `deleteSegment`…), поэтому тест команды покрывает оба пути.
- **Демо-данные как контракт.** Обработчики MSW прогоняются через типизированный клиент SPA
  (`mocks/handlers.test.ts`), а статусы ошибок сверяются с таблицей `ErrorCode` в `openapi.yaml`.
  Если демо-сервер отвечает не так, как ответит настоящий, это ловится здесь, а не на интеграции.
- **Поведение интерфейса.** Упор в 3,00 с для обычного стикера, слои окон, три тоста, «Вернуть» вместо подтверждения.
  Тест называется так, как это правило звучит для пользователя.

Тесты не нужны для разметки и стилей, для обёрток над библиотеками и для того, что уже ловит
сборка. Пример — `satisfies` в `ru.ts`: он гарантирует, что у каждого кода ошибки есть текст.

## Как пишем

- Файл лежит рядом с кодом: `crop.ts` → `crop.test.ts`. Среда — node, без jsdom.
- Имя теста описывает поведение, а не функцию: `stops O at 3,00 s and shows the hint`,
  а не `moveEdge returns limited`.
- Строгий TypeScript, как и весь клиент. `!` запрещён линтером, поэтому тип сужаем явно
  (`if (!x) throw …`).
- Фикстуры берём из демо-данных (`createDb("ok")`) и не выдумываем сцены вручную: так они остаются
  в рамках контракта.
- Глобальные сторы (`useEditor`, `useToasts`, `useOverlays`) сбрасываем в `beforeEach`.
- Сеть идёт через `msw/node`, время — через `vi.useFakeTimers()`. Настоящих запросов и ожиданий нет.
- Тест не пропускаем и не ослабляем, чтобы CI позеленел. Если поведение поменялось по решению,
  тест меняется вместе с кодом в том же PR.

## Компонентные тесты

Нужны там, где логика живёт в хуках и реакциях на события: слои окон, оптимистичные мутации,
действия «Проектов». Пример — `features/projects/actions.test.tsx`.

- Файл `*.test.tsx` рядом с компонентом, первая строка — `// @vitest-environment happy-dom`.
  Остальные тесты остаются в node. Версия Node для проекта задана в `.nvmrc`.
- Первым импортируется `src/test/dom.tsx`: он поднимает демо-сервер на `msw/node`, после каждого
  теста размонтирует компоненты, закрывает окна и сбрасывает сторы. Сцена берётся так:
  `useMockServer("ok")`, провайдеры — `withProviders()`.
- Ищем элементы так, как их видит пользователь: `getByRole` с подписью из `ru.ts`. Действия — через
  `userEvent`; с фейковыми таймерами — `userEvent.setup({ advanceTimers: vi.advanceTimersByTime })`.
- Демо-сервер ждёт по-настоящему (чтение файла — 1,5 с), поэтому в таких тестах
  `vi.useFakeTimers({ shouldAdvanceTime: true })` и `vi.advanceTimersByTimeAsync`.

## E2E

Несколько длинных путей пользователя в настоящем браузере: `src/web/e2e/*.spec.ts`. Playwright
сам поднимает `vite` на порту 5199 (`E2E_PORT`, если занят) и открывает `/?mock=<сценарий>`, сервер
не нужен. Сценарий — путь, который пользователь проходит целиком; мелкие ветки остаются юнит- и
компонентным тестам. Перед новым сценарием проверяем, нельзя ли поймать то же ниже по пирамиде.

### E2E на настоящем сервере

`src/web/e2e-server/*.spec.ts`, конфиг `playwright.server.config.ts`. Здесь только критический путь,
ради которого есть приложение: открыть видео → правка → точное превью → экспорт WEBM, который
проходит локальный валидатор стикеров, и проект на месте после перезагрузки. Это не проверка
публикации в живом Telegram. Всё это без моков: ASP.NET в
Production на чистом каталоге данных (`StickerSnap__DataDir` во временной папке), production-сборка
SPA из его `wwwroot` (поэтому сначала `npm run build`) и закреплённый FFmpeg из
`tools/ffmpeg/<rid>`. Тестовый ролик `e2e-server/start.mjs` делает тем же FFmpeg, в git видео нет.
Прогресс задач должен приходить через хаб: тест падает, если SPA перешла на опрос `/jobs/{id}`.
Порт 5198 (`E2E_SERVER_PORT`). Ветки пути (ошибки, пустые состояния, сценарии) остаются в `e2e/`
на демо-данных: настоящий рендер идёт секунды, и каждый лишний тест здесь замедляет весь пайплайн.

## Десктоп

`src/desktop`: логика оболочки (порт, запуск сервера, сохранённое состояние, что окну можно
открыть) — юнит-тесты на Vitest в node рядом с кодом, `npm test -w src/desktop`, job «Desktop
shell tests» в `Pipeline`. Сервер в них подменяет node-скрипт, Electron не нужен.

Смоук-e2e `src/desktop/e2e` запускает настоящее приложение через `_electron` Playwright: окно,
сервер, токен, выход. Нужны собранные SPA и сервер (`npm run build`, `npm run server -w
src/desktop`); `STICKERSNAP_APP` указывает на исполняемый файл собранного приложения, без него
запускается `electron .`. На macOS это файл внутри `.app/Contents/MacOS/`.
В CI это workflow «Build app» на Windows, Linux и macOS (Apple Silicon и Intel): он собирает
установщики и запускает smoke E2E после успешного `Pipeline` для того же коммита.
Platform для Electron проверяется в SPA (`shared/platform/electron.test.ts`) на поддельном мосте.

## Сервер

`src/server/StickerSnap.Tests`: xUnit v3 на Microsoft.Testing.Platform (включён в `src/server/global.json`),
проверки на AwesomeAssertions (`x.Should().Be(...)`), подмены на NSubstitute, время на `FakeTimeProvider`.
Папки повторяют слои: `Core`, `Application`, `Infrastructure`, `Api`, плюс `Architecture`.

- **Юнит-тесты — основание пирамиды.** Правила домена в `Core` и сценарии `Application` проверяются
  без диска, сети и процессов: репозитории и порты подменяются. Таймеры и задержки идут через
  `TimeProvider`, тест двигает время сам, а не ждёт.
- **Интеграционные — по одному на адаптер или эндпоинт.** `Infrastructure` — на временном каталоге;
  `Api` — через `ApiFactory` (`WebApplicationFactory<Program>` в окружении Testing со своим `wwwroot`,
  сборка SPA не нужна). Эндпоинт приходит вместе со своими тестами.
- **Медленные помечены `[Trait("Category", Category.Slow)]`.** Настоящий FFmpeg на файлах, всё, что
  идёт секунды. В `Pipeline` job «Server tests» запускает готовый xUnit v3 executable:
  `dotnet StickerSnap.Tests.dll -trait- "Category=Slow"`. Job «Slow server tests» того же workflow
  запускает `dotnet StickerSnap.Tests.dll -trait "Category=Slow" -trait- "Category=Online"`
  с закреплёнными FFmpeg и yt-dlp после быстрых тестов. yt-dlp в этом прогоне скачивает ролик
  с локального HTTP-сервера. Обе команды выполняются из `src/server/StickerSnap.Tests/bin/Release/net10.0`.
- **Online-тесты — отдельный ручной прогон.** `Infrastructure/Links/OnlineSitesTests` ходят в
  настоящий YouTube и Giphy, имеют оба тега `Slow` и `Online`; CI их не запускает.
  Из `src/server`: `dotnet test --solution StickerSnap.slnx --filter-trait Category=Online`.
  Для полного прогона без внешних сайтов используется `--filter-not-trait Category=Online`;
  без фильтра `dotnet test` запускает и Online-тесты. Успех локальных slow-тестов не подтверждает
  работу внешних сайтов или публикацию в Telegram.
- **Архитектура проверяется тестами.** `Architecture/LayerTests` (ArchUnitNET) падают, если `Core`
  знает о другом слое или фреймворке, `Application` — об инфраструктуре, а `Api` использует
  `Infrastructure` где-то кроме `Program`.
