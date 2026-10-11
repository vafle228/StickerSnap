import { HOTKEYS, type Hotkey, type HotkeyScope } from "../../shared/hotkeys";
import type { AppInfo } from "../../shared/api/types";
import { t } from "../../shared/strings/ru";

// What the Settings sections show, apart from the components: tested without a DOM.

/** What «Скопировать версии» puts on the clipboard: one line for a bug report. */
export function versionsLine(app: AppInfo, platform: string): string {
  const { status, version } = app.ffmpeg;
  const ffmpeg = status === "ok" ? version : [version, status].filter(Boolean).join(" ");
  return [
    `StickerSnap ${app.appVersion}`,
    `FFmpeg ${ffmpeg}`,
    `API ${app.apiVersion}`,
    app.mode,
    platform,
  ].join(" · ");
}

/** FFmpeg in «О программе»: its version, or why there is none to render with. */
export function ffmpegText(ffmpeg: AppInfo["ffmpeg"]): string {
  if (ffmpeg.status === "missing") return t("settings.about.ffmpegMissing");
  if (ffmpeg.status === "too_old") {
    return t("settings.about.ffmpegTooOld", { version: ffmpeg.version ?? "" });
  }
  return ffmpeg.version ?? "";
}

const SCOPES: readonly HotkeyScope[] = ["editor", "projects"];

/** The registry minus reserved keys, grouped by where they work. */
export function hotkeysByScope(): { scope: HotkeyScope; keys: Hotkey[] }[] {
  const all: Hotkey[] = Object.values(HOTKEYS);
  return SCOPES.map((scope) => ({
    scope,
    keys: all.filter((k) => k.scope === scope && !k.reserved),
  })).filter((group) => group.keys.length > 0);
}

/** The open-source parts StickerSnap ships with, by the license each comes under. */
export const LICENSES: readonly { name: string; license: string; url: string }[] = [
  { name: "FFmpeg", license: "LGPL 2.1+", url: "https://ffmpeg.org/legal.html" },
  { name: "Electron", license: "MIT", url: "https://github.com/electron/electron" },
  { name: "React", license: "MIT", url: "https://github.com/facebook/react" },
  { name: "ASP.NET Core", license: "MIT", url: "https://github.com/dotnet/aspnetcore" },
  { name: "SignalR", license: "MIT", url: "https://github.com/dotnet/aspnetcore" },
  { name: "TanStack Query", license: "MIT", url: "https://github.com/TanStack/query" },
  { name: "Zustand", license: "MIT", url: "https://github.com/pmndrs/zustand" },
  {
    name: "openapi-fetch",
    license: "MIT",
    url: "https://github.com/openapi-ts/openapi-typescript",
  },
  { name: "Manrope", license: "SIL OFL 1.1", url: "https://github.com/sharanda/manrope" },
  {
    name: "JetBrains Mono",
    license: "SIL OFL 1.1",
    url: "https://github.com/JetBrains/JetBrainsMono",
  },
];
