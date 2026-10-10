import { create } from "zustand";
import { persist } from "zustand/middleware";

export type Section = "editor" | "projects" | "packs" | "settings";

interface AppState {
  section: Section;
  setSection: (section: Section) => void;
}

// No router: the open section lives here and survives F5.
export const useApp = create<AppState>()(
  persist(
    (set) => ({
      section: "editor",
      setSection: (section) => set({ section }),
    }),
    {
      name: "stickersnap.app",
      partialize: ({ section }) => ({ section }),
    },
  ),
);
