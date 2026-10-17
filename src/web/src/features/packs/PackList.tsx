import { Badge } from "@stickersnap/ds";
import type { PackSummary } from "../../shared/api/types";
import { t } from "../../shared/strings/ru";
import { packMeta } from "./format";
import styles from "./PacksScreen.module.css";
import { StickerMedia } from "./StickerMedia";

interface PackListProps {
  packs: PackSummary[];
  current: string;
  onOpen: (name: string) => void;
}

/** The left column of artboard 7: every pack with its fill, the open one highlighted. */
export function PackList({ packs, current, onOpen }: PackListProps) {
  return (
    <nav className={styles.list} aria-label={t("packs.list")}>
      <p className={styles.listCount}>{t("packs.count", { count: packs.length })}</p>
      <ul className={styles.listItems}>
        {packs.map((p, i) => {
          const meta = packMeta(p);
          return (
            <li key={p.name}>
              <button
                type="button"
                className={styles.pack}
                aria-current={p.name === current ? "page" : undefined}
                onClick={() => onOpen(p.name)}
              >
                <span className={styles.packCover} data-gone={p.status === "missing"} aria-hidden>
                  <StickerMedia src={p.coverUrl} index={i} shapeClassName={styles.shape} />
                </span>
                <span className={styles.packText}>
                  <span className={styles.packTitle}>{p.title}</span>
                  <span className={styles.packMeta} data-tone={meta.tone}>
                    {meta.text}
                  </span>
                </span>
                <Badge>{t(`packs.kind.${p.type}`)}</Badge>
              </button>
            </li>
          );
        })}
      </ul>
      <p className={styles.listNote}>{t("packs.listNote")}</p>
    </nav>
  );
}
