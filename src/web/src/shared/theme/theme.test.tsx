// @vitest-environment happy-dom
import { act, renderHook, waitFor } from "@testing-library/react";
import { http } from "msw";
import { afterEach, describe, expect, it, vi } from "vitest";
import { problem } from "../../mocks/route";
import { server, useMockServer, withProviders } from "../../test/dom";
import { useToasts } from "../notify";
import { useSettings } from "../api/queries";
import { useApplyTheme, useSetTheme, useTheme } from "./theme";

/* «Вид» in Settings (R20, R22): no «Сохранить» — a change is on screen at once and goes to the
   server by PATCH; a failed PATCH puts the old look back. «Как в системе» follows the OS. */

/** prefers-color-scheme the test can flip, as the OS does. */
function fakeOs(dark: boolean) {
  const listeners = new Set<() => void>();
  const query = {
    get matches() {
      return dark;
    },
    addEventListener: (_: string, fn: () => void) => listeners.add(fn),
    removeEventListener: (_: string, fn: () => void) => listeners.delete(fn),
  };
  vi.stubGlobal("matchMedia", () => query);
  return (next: boolean) => {
    dark = next;
    listeners.forEach((fn) => fn());
  };
}

function look() {
  return renderHook(
    () => {
      useApplyTheme();
      return { settings: useSettings().data, setTheme: useSetTheme() };
    },
    { wrapper: withProviders() },
  );
}

const html = () => document.documentElement;

afterEach(() => {
  vi.unstubAllGlobals();
  useTheme.setState({ mode: "system", accent: "indigo" });
});

describe("theme", () => {
  it("follows the OS in «Как в системе»", async () => {
    const setOs = fakeOs(false);
    const db = useMockServer("ok");
    db.settings = { ...db.settings, theme: "system" };
    const { result } = look();
    await waitFor(() => expect(result.current.settings).toBeDefined());
    expect(html().dataset.theme).toBe("light");

    act(() => setOs(true));
    expect(html().dataset.theme).toBe("dark");
  });

  it("applies a new theme and accent at once and saves them by PATCH", async () => {
    fakeOs(false);
    const db = useMockServer("ok");
    const { result } = look();
    await waitFor(() => expect(result.current.settings).toBeDefined());

    act(() => result.current.setTheme({ theme: "dark", accent: "teal" }));
    expect(html().dataset.theme).toBe("dark");
    expect(html().classList.contains("ss-accent-teal")).toBe(true);
    // No transitions while the window repaints, back after two frames
    expect(html().classList.contains("ss-no-motion")).toBe(true);
    await waitFor(() => expect(html().className).toBe("ss-accent-teal"));
    await waitFor(() => expect(db.settings).toMatchObject({ theme: "dark", accent: "teal" }));
  });

  it("puts the old theme back and says so when the PATCH fails", async () => {
    fakeOs(false);
    const db = useMockServer("ok");
    db.settings = { ...db.settings, theme: "light" };
    server.use(http.patch("*/api/v1/settings", () => problem("save_failed")));
    const { result } = look();
    await waitFor(() => expect(result.current.settings?.theme).toBe("light"));

    act(() => result.current.setTheme({ theme: "dark" }));
    expect(html().dataset.theme).toBe("dark");

    await waitFor(() => expect(html().dataset.theme).toBe("light"));
    expect(result.current.settings?.theme).toBe("light");
    expect(useToasts.getState().toasts.map((t) => t.key)).toEqual(["settings"]);
  });
});
