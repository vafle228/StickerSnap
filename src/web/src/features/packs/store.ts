import { create } from "zustand";
import { persist } from "zustand/middleware";

/** What the publish window opens with (artboard 7, «Откуда открывается окно публикации»). */
export interface PublishStart {
  /** Chosen beforehand; otherwise the window asks for a project first */
  projectId?: string;
  /** «В мой пак»: this pack is picked */
  pack?: string;
  /** «Новый пак» is picked */
  newPack?: boolean;
  /** «Заменить новой версией…»: only this segment, in place of this sticker */
  replace?: { segmentId: string; stickerId: string; emoji: string[] };
}

interface PacksState {
  /** The pack open on the right; the first one when none is */
  open?: string;
  publish: PublishStart | null;
  openPack: (name: string) => void;
  startPublish: (start: PublishStart) => void;
  closePublish: () => void;
}

export const usePacksUi = create<PacksState>()(
  persist(
    (set) => ({
      publish: null,
      openPack: (open) => set({ open }),
      startPublish: (publish) => set({ publish }),
      closePublish: () => set({ publish: null }),
    }),
    { name: "stickersnap.packs", partialize: ({ open }) => ({ open }) },
  ),
);
