import { create } from "zustand";
import type { LocalFile } from "../../shared/platform";

/** A file is being read (artboard 3c, A): a video into a new project, or a .webm for the check. */
export interface Reading {
  name: string;
  kind: "video" | "webm";
  /** Reading has taken over 300 ms: only then the spinner shows, so a quick read never blinks */
  slow: boolean;
  abort: AbortController;
}

export const useImport = create<{ reading?: Reading }>()(() => ({}));

const SLOW_MS = 300;

/** «Отменить» of «Читаю файл»: the request is dropped (contract, POST /api/v1/projects). */
export function cancelReading() {
  useImport.getState().reading?.abort.abort();
}

const isAbort = (error: unknown) => error instanceof DOMException && error.name === "AbortError";

/** Runs `work` as the one file being read; undefined when another read is on or it was canceled. */
export async function readFile<T>(
  file: LocalFile,
  kind: Reading["kind"],
  work: (signal: AbortSignal) => Promise<T>,
): Promise<{ value: T } | { error: unknown } | undefined> {
  if (useImport.getState().reading) return undefined;
  const abort = new AbortController();
  useImport.setState({ reading: { name: file.name, kind, slow: false, abort } });
  const timer = setTimeout(() => {
    const now = useImport.getState().reading;
    if (now?.abort === abort) useImport.setState({ reading: { ...now, slow: true } });
  }, SLOW_MS);
  try {
    return { value: await work(abort.signal) };
  } catch (error) {
    return abort.signal.aborted || isAbort(error) ? undefined : { error };
  } finally {
    clearTimeout(timer);
    useImport.setState({ reading: undefined });
  }
}
