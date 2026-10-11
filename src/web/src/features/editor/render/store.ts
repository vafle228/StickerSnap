import { create } from "zustand";
import type { ExportResult, Segment } from "../../../shared/api/types";

/* The two renders of artboard 3c: the exact preview (D) and the export (E–H). Both are jobs of
   the server; this store keeps which job belongs to the open project and what it was started
   for, so a result is shown only while its segment is the same as when it was rendered. */

/** A segment as it was rendered: any edit after that makes the result stale. */
export function signature(segment: Segment): string {
  return JSON.stringify(segment);
}

export interface Rendered {
  /** signature() of the segment the file was made from */
  of: string;
}

interface RenderState {
  /** «Точное превью» on the glass bar: every edit renders a new draft */
  exact: boolean;
  preview?: Rendered & { jobId: string };
  /** The finished draft, played from memory */
  previewFile?: Rendered & { url: string; bytes: number };
  /** The running export of this project: one job for one segment or for all of them */
  exporting?: { jobId: string; segmentIds: string[] };
  /** The last export of each segment; shown in the inspector until the next edit (3c, H) */
  results: Record<string, Rendered & { result: ExportResult }>;
  /** «Сравнить с исходником»: the canvas shows the source and the result side by side */
  compare: boolean;

  setExact: (exact: boolean) => void;
  setCompare: (compare: boolean) => void;
  reset: (results?: RenderState["results"]) => void;
}

function revoke(file: RenderState["previewFile"]) {
  if (file) URL.revokeObjectURL(file.url);
}

export const useRender = create<RenderState>()((set, get) => ({
  exact: false,
  results: {},
  compare: false,
  setExact: (exact) => set(exact ? { exact } : { exact, preview: undefined }),
  setCompare: (compare) => set({ compare }),
  reset: (results = {}) => {
    revoke(get().previewFile);
    set({
      exact: false,
      preview: undefined,
      previewFile: undefined,
      exporting: undefined,
      results,
      compare: false,
    });
  },
}));

/** Swaps in a new draft and frees the old one's memory. */
export function setPreviewFile(file: RenderState["previewFile"]) {
  revoke(useRender.getState().previewFile);
  useRender.setState({ previewFile: file });
}

/** The fresh export of `segment`, if its result still matches it. */
export function freshResult(segment: Segment | undefined): ExportResult | undefined {
  if (!segment) return undefined;
  const entry = useRender.getState().results[segment.id];
  return entry && entry.of === signature(segment) ? entry.result : undefined;
}

export function useFreshResult(segment: Segment | undefined): ExportResult | undefined {
  return useRender((s) => {
    if (!segment) return undefined;
    const entry = s.results[segment.id];
    return entry && entry.of === signature(segment) ? entry.result : undefined;
  });
}
