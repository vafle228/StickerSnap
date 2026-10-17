import type { ReactNode } from "react";
import { Card } from "@stickersnap/ds";
import styles from "./SettingsScreen.module.css";

export type SectionId = "bot" | "look" | "folders" | "keys" | "about";

/** One section of the scroll: a card with a title and an optional line under it. */
export function SettingsSection({
  id,
  title,
  text,
  children,
}: {
  id: SectionId;
  title: string;
  text?: string;
  children: ReactNode;
}) {
  return (
    <Card id={id} aria-labelledby={`${id}-title`} className={styles.section}>
      <div className={styles.head}>
        <h2 id={`${id}-title`} className={styles.sectionTitle}>
          {title}
        </h2>
        {text && <p className={styles.text}>{text}</p>}
      </div>
      {children}
    </Card>
  );
}
