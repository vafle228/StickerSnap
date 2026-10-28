import { useState, type FormEvent } from "react";
import { Button, Dialog, TextField } from "@stickersnap/ds";
import { notify, notifyProblem, PROBLEMS, problemText, toProblem } from "../../shared/notify";
import { t } from "../../shared/strings/ru";
import { usePackActions } from "./actions";

/** «Добавить по ссылке…» (artboards 7, 12): a pack made by our bot onto the list, checked with
 *  Telegram. A pack the bot may not change (from @Stickers, another bot, or one the cloud service
 *  does not know) turns the window into «Скопировать в StickerSnap»: the same stickers in a new
 *  pack of ours. What is wrong with the link or the title shows under the field; a failure of the
 *  app is a popup. */
export function AddByLinkDialog({ onClose }: { onClose: () => void }) {
  const actions = usePackActions();
  const [link, setLink] = useState("");
  // Set once the link names a pack the bot may not change: the window offers a copy
  const [copying, setCopying] = useState(false);
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const fail = (failure: unknown, key: string) => {
    const problem = toProblem(failure);
    if (problem.code === "bad_request")
      setError(t(copying ? "packs.copy.noTitle" : "packs.byLink.noPack"));
    else if (PROBLEMS[problem.code].form !== "toast") setError(problemText(problem).title);
    else notifyProblem(failure, { key });
  };

  const add = async () => {
    try {
      const pack = await actions.importPack(link.trim());
      notify.toast({ tone: "success", title: t("packs.byLink.added", { title: pack.title }) });
      onClose();
    } catch (failure) {
      const code = toProblem(failure).code;
      if (code === "not_our_pack" || code === "pack_not_found") setCopying(true);
      else fail(failure, "pack-import");
    }
  };

  const copy = async () => {
    try {
      const pack = await actions.copyPack(link.trim(), title.trim());
      notify.toast({ tone: "success", title: t("packs.copy.done", { title: pack.title }) });
      onClose();
    } catch (failure) {
      fail(failure, "pack-copy");
    }
  };

  const ready = copying ? Boolean(title.trim()) : Boolean(link.trim());
  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!ready || busy) return;
    setBusy(true);
    try {
      await (copying ? copy() : add());
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      title={t(copying ? "packs.copy.title" : "packs.byLink.title")}
      text={t(copying ? "packs.copy.text" : "packs.byLink.text")}
      onClose={onClose}
      actions={
        <>
          <Button onClick={onClose}>{t("common.cancel")}</Button>
          <Button variant="primary" type="submit" form="pack-by-link" disabled={!ready || busy}>
            {copying ? t(busy ? "packs.copy.busy" : "packs.copy.action") : t("packs.byLink.add")}
          </Button>
        </>
      }
    >
      <form id="pack-by-link" onSubmit={(e) => void submit(e)}>
        {copying ? (
          <TextField
            key="title"
            aria-label={t("packs.copy.label")}
            placeholder={t("packs.copy.placeholder")}
            value={title}
            maxLength={64}
            error={error}
            disabled={busy}
            autoFocus
            onChange={(e) => {
              setTitle(e.target.value);
              setError(undefined);
            }}
          />
        ) : (
          <TextField
            key="link"
            aria-label={t("packs.byLink.label")}
            placeholder="t.me/addstickers/…"
            value={link}
            error={error}
            data-autofocus
            onChange={(e) => {
              setLink(e.target.value);
              setError(undefined);
            }}
          />
        )}
      </form>
    </Dialog>
  );
}
