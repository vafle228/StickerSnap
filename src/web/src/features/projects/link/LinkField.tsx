import type { FormEvent } from "react";
import { Badge, Button, TextField } from "@stickersnap/ds";
import { cx } from "@stickersnap/ds/cx";
import { t } from "../../../shared/strings/ru";
import { siteName } from "./format";
import styles from "./Link.module.css";
import { setLinkText, useLinkActions, useLinkImport } from "./linkImport";
import { recognize } from "./parse";

export interface LinkFieldProps {
  /** inline: «Открыть» beside the field, in the start screen's zone (artboard 20·2).
   *  stacked: «Отмена» and «Открыть» under it, in the tile's popover */
  layout?: "inline" | "stacked";
  /** «Отмена» of the popover */
  onCancel?: () => void;
  autoFocus?: boolean;
  className?: string;
}

/** The link field of artboard 20, section 3: «Открыть» is off while it is empty, Enter opens.
 *  The source badge shows as soon as the link looks like YouTube or Giphy (3·2); what is wrong
 *  with a link shows under it after «Открыть» (3·3, 3·7) and goes on the next keystroke. */
export function LinkField({ layout = "inline", onCancel, autoFocus, className }: LinkFieldProps) {
  const text = useLinkImport((s) => s.text);
  const error = useLinkImport((s) => s.error);
  const probing = useLinkImport((s) => s.probing !== undefined);
  const { open } = useLinkActions();
  const known = recognize(text);
  const stacked = layout === "stacked";

  const submit = (e: FormEvent) => {
    e.preventDefault();
    void open();
  };

  const openButton = (
    <Button
      type="submit"
      variant={stacked ? "primary" : "secondary"}
      size={stacked ? "sm" : "md"}
      disabled={!text.trim() || probing}
    >
      {t("link.open")}
    </Button>
  );

  return (
    <form className={cx(styles.form, stacked && styles.stacked, className)} onSubmit={submit}>
      <div className={styles.row}>
        <TextField
          className={styles.field}
          icon="link"
          type="url"
          aria-label={t("link.label")}
          placeholder={t("link.placeholder")}
          value={text}
          error={error}
          hint={stacked ? undefined : known ? t(`link.kind.${known.kind}`) : t("link.hint")}
          autoFocus={autoFocus}
          data-autofocus={autoFocus || undefined}
          spellCheck={false}
          onChange={(e) => setLinkText(e.target.value)}
          trailing={known && <Badge tone="neutral">{siteName(known.site)}</Badge>}
        />
        {!stacked && openButton}
      </div>
      {stacked && (
        <div className={styles.actions}>
          <Button size="sm" variant="ghost" onClick={onCancel}>
            {t("common.cancel")}
          </Button>
          {openButton}
        </div>
      )}
    </form>
  );
}
