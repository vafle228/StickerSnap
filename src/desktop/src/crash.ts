import type { LastCrash } from "./bridge.js";
import type { ShellState } from "./state.js";

/** How much of the server's output a crash keeps: the end, where the exception is. */
export const OUTPUT_TAIL = 4000;

/** The crash the last launch left, if any (artboard 19, section 4: «Прошлый запуск завершился
 *  ошибкой» at the next start). A launch marks itself running and unmarks on a clean quit, so a
 *  shell that died without quitting is found by the mark it left. */
export function lastCrash(state: ShellState): LastCrash | undefined {
  if (state.crash) return state.crash;
  if (state.running) return { kind: "shell", at: state.startedAt ?? new Date(0).toISOString() };
  return undefined;
}

/** This launch begins: the last one's crash is taken out of the state, and the mark is set. */
export function beginLaunch(state: ShellState, now: Date): LastCrash | undefined {
  const crash = lastCrash(state);
  delete state.crash;
  state.running = true;
  state.startedAt = now.toISOString();
  return crash;
}

/** The server stopped by itself: its exit code and the end of what it printed. */
export function serverCrash(code: number | null, output: string, now: Date): LastCrash {
  return { kind: "server", at: now.toISOString(), code, output: output.slice(-OUTPUT_TAIL) };
}

/** The window's renderer died: its reason, as Electron names it (crashed, oom, killed). */
export function windowCrash(reason: string, now: Date): LastCrash {
  return { kind: "window", at: now.toISOString(), reason };
}
