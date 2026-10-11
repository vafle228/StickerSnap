import { useEffect } from "react";
import { create } from "zustand";
import { useLatest } from "../../ds/hooks";

/** A file is carried over the window (artboard 3c, 0): every import zone turns its dashes into
 *  the rainbow at once, not only the one under the pointer. */
export const useFileDrag = create<{ over: boolean }>()(() => ({ over: false }));

const carriesFiles = (e: DragEvent) => e.dataTransfer?.types.includes("Files") ?? false;

/** Mounted once at the root. A file dropped anywhere on the window opens (`onFile`) unless an
 *  import zone took it first; either way the browser never leaves the app to show the file. */
export function useWindowDrop(onFile: (file: File) => void) {
  const open = useLatest(onFile);
  useEffect(() => {
    // dragenter and dragleave come for every element crossed; the window is left at zero.
    let depth = 0;
    const set = (over: boolean) => {
      if (useFileDrag.getState().over !== over) useFileDrag.setState({ over });
    };
    const enter = (e: DragEvent) => {
      if (!carriesFiles(e)) return;
      depth += 1;
      set(true);
    };
    const leave = (e: DragEvent) => {
      if (!carriesFiles(e)) return;
      depth = Math.max(0, depth - 1);
      if (depth === 0) set(false);
    };
    const over = (e: DragEvent) => e.preventDefault();
    const drop = (e: DragEvent) => {
      depth = 0;
      set(false);
      // An import zone handled it already (React's listener runs before the window's).
      if (e.defaultPrevented) return;
      e.preventDefault();
      const file = e.dataTransfer?.files[0];
      if (file) open.current(file);
    };
    window.addEventListener("dragenter", enter);
    window.addEventListener("dragleave", leave);
    window.addEventListener("dragover", over);
    window.addEventListener("drop", drop);
    return () => {
      window.removeEventListener("dragenter", enter);
      window.removeEventListener("dragleave", leave);
      window.removeEventListener("dragover", over);
      window.removeEventListener("drop", drop);
    };
  }, [open]);
}
