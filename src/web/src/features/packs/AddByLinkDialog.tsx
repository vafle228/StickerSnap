import { useState, type FormEvent } from "react";
import { Button, Dialog, TextField } from "@stickersnap/ds";
import { notify, notifyProblem, PROBLEMS, problemText, toProblem } from "../../shared/notify";
import { t } from "../../shared/strings/ru";
import { usePackActions } from "./actions";

/** «Добавить по ссылке…» (artboards 7, 12): a pack made by our bot onto the list, checked with
 *  Telegram. What is wrong with the link shows under the field; a failure of the app is a popup. */
export function AddByLinkDialog({ onClose }: { onClose: () => void }) {
  const actions = usePackActions();
  const [link, setLink] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (!link.trim() || busy) return;
    setBusy(true);
    try {
      const pack = await actions.importPack(link.trim());
      notify.toast({ tone: "success", title: t("packs.byLink.added", { title: pack.title }) });
      onClose();
    } catch (failure) {
      const problem = toProblem(failure);
      if (problem.code === "bad_request") setError(t("packs.byLink.noPack"));
      else if (PROBLEMS[problem.code].form !== "toast") setError(problemText(problem).title);
      else notifyProblem(failure, { key: "pack-import" });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      title={t("packs.byLink.title")}
      text={t("packs.byLink.text")}
      onClose={onClose}
      actions={
        <>
          <Button onClick={onClose}>{t("common.cancel")}</Button>
          <Button
            variant="primary"
            type="submit"
            form="pack-by-link"
            disabled={!link.trim() || busy}
          >
            {t("packs.byLink.add")}
          </Button>
        </>
      }
    >
      <form id="pack-by-link" onSubmit={(e) => void submit(e)}>
        <TextField
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
      </form>
    </Dialog>
  );
}
