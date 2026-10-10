import { useEffect } from "react";
import { create } from "zustand";
import { persist } from "zustand/middleware";
import type { AccentName } from "../../ds/accents";

export type ThemeMode = "system" | "light" | "dark";
export type { AccentName };

export const THEME_MODES: readonly ThemeMode[] = ["system", "light", "dark"];

interface ThemeState {
  mode: ThemeMode;
  accent: AccentName;
  setMode: (mode: ThemeMode) => void;
  setAccent: (accent: AccentName) => void;
}

// Until the settings API is wired, the choice lives in localStorage.
// index.html reads the same key before the first paint: keep both in sync.
export const useTheme = create<ThemeState>()(
  persist(
    (set) => ({
      mode: "system",
      accent: "indigo",
      setMode: (mode) => set({ mode }),
      setAccent: (accent) => set({ accent }),
    }),
    {
      name: "stickersnap.theme",
      partialize: ({ mode, accent }) => ({ mode, accent }),
    },
  ),
);

const darkQuery = () => window.matchMedia("(prefers-color-scheme: dark)");

function applyTheme(mode: ThemeMode, accent: AccentName) {
  const dark = mode === "dark" || (mode === "system" && darkQuery().matches);
  const root = document.documentElement;
  root.dataset.theme = dark ? "dark" : "light";
  root.className = `ss-accent-${accent}`;
}

/** Keeps data-theme and the accent class on <html> in step with the store and the OS. */
export function useApplyTheme() {
  const mode = useTheme((s) => s.mode);
  const accent = useTheme((s) => s.accent);

  useEffect(() => {
    applyTheme(mode, accent);
    if (mode !== "system") return undefined;
    const query = darkQuery();
    const onChange = () => applyTheme(mode, accent);
    query.addEventListener("change", onChange);
    return () => query.removeEventListener("change", onChange);
  }, [mode, accent]);
}
