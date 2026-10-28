import { useQueryClient } from "@tanstack/react-query";
import { useMemo } from "react";
import { api, call } from "../../shared/api/client";
import { applyJob } from "../../shared/api/live";
import { keys } from "../../shared/api/queries";
import type { JobRequest, Pack, PackSticker, PackSummary, Project } from "../../shared/api/types";
import { confirm, notify, notifyProblem } from "../../shared/notify";
import { platform } from "../../shared/platform";
import { t } from "../../shared/strings/ru";
import { flushSave } from "../editor/model/autosave";
import { useEditor } from "../editor/model/store";
import { usePacksUi } from "./store";

/** What the window sends; the revision is added at the moment of sending. */
export type PublishRequest = Omit<Extract<JobRequest, { kind: "publish" }>, "kind" | "revision">;

/** What «Мои паки» does with packs and their stickers (artboard 7). Every action reports its
 *  own failure as the popup of artboard 8; none throws. */
export function usePackActions() {
  const client = useQueryClient();

  return useMemo(() => {
    const putPack = (pack: Pack) => {
      client.setQueryData(keys.pack(pack.name), pack);
      client.setQueryData<PackSummary[]>(keys.packs, (list) =>
        list?.map((p) => (p.name === pack.name ? { ...p, ...pack } : p)),
      );
    };

    /** «обновить», and opening a pack: the pack as Telegram has it now. On opening a failure
     *  stays quiet, the pack shows its last check. */
    const sync = async (name: string, { quiet = false } = {}) => {
      try {
        putPack(
          await call(api.POST("/api/v1/packs/{pack}/sync", { params: { path: { pack: name } } })),
        );
      } catch (error) {
        if (!quiet) notifyProblem(error, { key: "pack-sync" });
      }
    };

    /** A pack the server answered with onto the list, opened. */
    const putNew = (pack: Pack) => {
      client.setQueryData(keys.pack(pack.name), pack);
      // A Pack is a PackSummary with its stickers, as putPack keeps it too
      client.setQueryData<PackSummary[]>(keys.packs, (list = []) =>
        list.some((p) => p.name === pack.name)
          ? list.map((p) => (p.name === pack.name ? { ...p, ...pack } : p))
          : [...list, pack],
      );
      usePacksUi.getState().openPack(pack.name);
      return pack;
    };

    /** «Добавить по ссылке…»: throws, so the window can show what is wrong with the link. */
    const importPack = async (link: string) =>
      putNew(await call(api.POST("/api/v1/packs/import", { body: { link } })));

    /** «Скопировать в StickerSnap»: a pack the bot may not change, copied into a new one of ours.
     *  Throws, so the window can show what went wrong. */
    const copyPack = async (link: string, title: string) =>
      putNew(await call(api.POST("/api/v1/packs/copy", { body: { link, title } })));

    /** «Убрать из списка» (7-8): the app forgets the pack, Telegram is not touched. */
    const forget = async (pack: PackSummary) => {
      try {
        await call(api.DELETE("/api/v1/packs/{pack}", { params: { path: { pack: pack.name } } }));
      } catch (error) {
        notifyProblem(error, { key: "pack-forget" });
        return;
      }
      client.setQueryData<PackSummary[]>(keys.packs, (list) =>
        list?.filter((p) => p.name !== pack.name),
      );
      client.removeQueries({ queryKey: keys.pack(pack.name) });
      notify.toast({
        tone: "info",
        title: t("packs.forgotten"),
        description: t("packs.forgottenText"),
      });
    };

    /** «Удалить из пака…» (7-7) asks first: only a new publish brings the sticker back. */
    const removeSticker = async (pack: string, sticker: PackSticker) => {
      const yes = await confirm({
        title: t("packs.removeSticker.title", { emoji: sticker.emoji.join(" ") }),
        text: t("packs.removeSticker.text"),
        confirmLabel: t("packs.removeSticker.confirm"),
        icon: "trash",
      });
      if (!yes) return;
      try {
        await call(
          api.DELETE("/api/v1/packs/{pack}/stickers/{stickerId}", {
            params: { path: { pack, stickerId: sticker.id } },
          }),
        );
        client.setQueryData<Pack>(
          keys.pack(pack),
          (p) =>
            p && {
              ...p,
              count: p.count - 1,
              stickers: p.stickers.filter((s) => s.id !== sticker.id),
            },
        );
        void client.invalidateQueries({ queryKey: keys.packs });
      } catch (error) {
        notifyProblem(error, { key: "pack-sticker-remove" });
      }
    };

    /** «Показать .webm в папке», desktop only: the last export of the sticker's segment. */
    const revealWebm = async (origin: NonNullable<PackSticker["origin"]>) => {
      try {
        const project = await client.fetchQuery({
          queryKey: keys.project(origin.projectId),
          queryFn: () =>
            call(api.GET("/api/v1/projects/{id}", { params: { path: { id: origin.projectId } } })),
          staleTime: Infinity,
        });
        const path = (project as Project).exports[origin.segmentId]?.path;
        if (path) platform.showInFolder(path);
        else
          notify.toast({
            tone: "info",
            title: t("packs.noWebm"),
            description: t("packs.noWebmText"),
          });
      } catch (error) {
        notifyProblem(error, { key: "pack-webm" });
      }
    };

    /** «Повторить сейчас» (7-6): send what is left without waiting for Telegram's timer. */
    const retry = async (jobId: string) => {
      try {
        applyJob(
          client,
          await call(api.POST("/api/v1/jobs/{jobId}/retry", { params: { path: { jobId } } })),
        );
      } catch (error) {
        notifyProblem(error, { key: "publish-retry" });
      }
    };

    /** «Опубликовать N стикеров» (artboard 7): a job by the saved revision, as an export goes.
     *  The project open in the editor is saved first; another one is read for its revision.
     *  Throws, so the window stays open with what was picked. */
    const publish = async (request: PublishRequest) => {
      const revision =
        useEditor.getState().projectId === request.projectId
          ? await flushSave()
          : (
              await call(
                api.GET("/api/v1/projects/{id}", { params: { path: { id: request.projectId } } }),
              )
            ).revision;
      const job = await call(
        api.POST("/api/v1/jobs", { body: { ...request, kind: "publish", revision } }),
      );
      applyJob(client, job);
      return job;
    };

    return { sync, importPack, copyPack, forget, removeSticker, revealWebm, retry, publish };
  }, [client]);
}
