import { create } from "zustand";
import { persist } from "zustand/middleware";

export type Section = "editor" | "projects" | "packs" | "settings";

interface AppState {
  section: Section;
  /** The project open in the editor; none means the start screen in place of the canvas */
  projectId?: string;
  setSection: (section: Section) => void;
  openProject: (id: string) => void;
  closeProject: () => void;
}

// No router: the open section and the open project live here and survive F5.
export const useApp = create<AppState>()(
  persist(
    (set) => ({
      section: "editor",
      setSection: (section) => set({ section }),
      openProject: (projectId) => set({ projectId, section: "editor" }),
      closeProject: () => set({ projectId: undefined }),
    }),
    {
      name: "stickersnap.app",
      partialize: ({ section, projectId }) => ({ section, projectId }),
    },
  ),
);
