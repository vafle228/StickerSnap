import { useEffect } from "react";
import { create } from "zustand";
import { persist } from "zustand/middleware";
import type { AccentName } from "../../ds/accents";
import { useSettings, useUpdateSettings } from "../api/queries";
import type { Accent, Theme } from "../api/types";

export type ThemeMode = Theme;
export type { AccentName };

export const THEME_MODES: readonly ThemeMode[] = ["system", "light", "dark"];

// The DS accents and the contract's Accent are one list: the build fails when they drift.
type Same<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;
true satisfies Same<AccentName, Accent>;

interface ThemeState {
  mode: ThemeMode;
  accent: AccentName;
}

/* The server's settings are the source of truth (GET /api/v1/settings, settingsChanged).
   This store mirrors them in localStorage only so index.html can set the theme before the
   first paint and a dark window never flashes light: keep the storage key and values in sync. */
export const useTheme = create<ThemeState>()(
  persist((): ThemeState => ({ mode: "system", accent: "indigo" }), {
    name: "stickersnap.theme",
  }),
);

const darkQuery = () => window.matchMedia("(prefers-color-scheme: dark)");

function applyTheme(mode: ThemeMode, accent: AccentName) {
  const dark = mode === "dark" || (mode === "system" && darkQuery().matches);
  const root = document.documentElement;
  root.dataset.theme = dark ? "dark" : "light";
  root.className = `ss-accent-${accent}`;
}

/** Keeps data-theme and the accent class on <html> in step with the server's settings and the OS. */
export function useApplyTheme() {
  const { data: settings } = useSettings();
  const mode = useTheme((s) => s.mode);
  const accent = useTheme((s) => s.accent);

  useEffect(() => {
    if (settings) useTheme.setState({ mode: settings.theme, accent: settings.accent });
  }, [settings]);

  useEffect(() => {
    applyTheme(mode, accent);
    if (mode !== "system") return undefined;
    const query = darkQuery();
    const onChange = () => applyTheme(mode, accent);
    query.addEventListener("change", onChange);
    return () => query.removeEventListener("change", onChange);
  }, [mode, accent]);
}

/** Changes the theme or the accent: on screen at once, on the server by PATCH. */
export function useSetTheme() {
  const { mutate } = useUpdateSettings();
  return (patch: { theme?: ThemeMode; accent?: AccentName }) => {
    useTheme.setState((s) => ({ mode: patch.theme ?? s.mode, accent: patch.accent ?? s.accent }));
    mutate(patch);
  };
}
