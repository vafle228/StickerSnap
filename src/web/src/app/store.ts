import { create } from "zustand";
import { persist } from "zustand/middleware";

export type Section = "editor" | "projects" | "packs" | "settings";

interface AppState {
  section: Section;
  /** The project open in the editor; none means the start screen in place of the canvas */
  projectId?: string;
  /** Where «Назад» in the editor's header leads: the section the project was opened from.
   *  "editor" means the start screen. */
  returnTo: Section;
  setSection: (section: Section) => void;
  openProject: (id: string) => void;
  closeProject: () => void;
  /** «Назад»: closes the project and goes back to where it was opened from */
  goBack: () => void;
}

// No router: the open section and the open project live here and survive F5.
export const useApp = create<AppState>()(
  persist(
    (set) => ({
      section: "editor",
      returnTo: "editor",
      setSection: (section) => set({ section }),
      openProject: (projectId) =>
        set((s) => ({
          projectId,
          section: "editor",
          // A project opened from inside the editor (an import, «Показать» on a popup) keeps
          // the way back of the one it replaces: the user is still on the same trip.
          returnTo: s.section === "editor" && s.projectId ? s.returnTo : s.section,
        })),
      closeProject: () => set({ projectId: undefined }),
      goBack: () => set((s) => ({ projectId: undefined, section: s.returnTo, returnTo: "editor" })),
    }),
    {
      name: "stickersnap.app",
      partialize: ({ section, projectId, returnTo }) => ({ section, projectId, returnTo }),
    },
  ),
);
