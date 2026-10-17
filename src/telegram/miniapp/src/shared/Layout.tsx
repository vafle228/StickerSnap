import type { ReactNode } from "react";
import { Icon, TONE_ICON, type Tone } from "@stickersnap/ds";
import styles from "./layout.module.css";

/** A screen's body: Telegram draws the header above it, so the page starts with content. */
export function Page({ label, children }: { label: string; children: ReactNode }) {
  return (
    <main className={`ss-root ${styles.body}`} aria-label={label}>
      {children}
    </main>
  );
}

export function PageHead({ title, sub }: { title: string; sub: string }) {
  return (
    <div className={styles.head}>
      <h1 className={styles.h1}>{title}</h1>
      <p className={styles.sub}>{sub}</p>
    </div>
  );
}

/** Rows of grey bars where a list will be (artboard 14, state 1) */
export function SkeletonRows({ count = 5, faded }: { count?: number; faded?: boolean }) {
  return (
    <div className={`${styles.list} ${faded ? styles.faded : ""}`} aria-hidden>
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className={styles.row}>
          <span className={styles.skeleton} style={{ width: 56, height: 56, borderRadius: 14 }} />
          <span className={styles.text} style={{ gap: 10 }}>
            <span className={styles.skeleton} style={{ width: "62%", height: 14 }} />
            <span className={styles.skeleton} style={{ width: "38%", height: 12 }} />
          </span>
        </div>
      ))}
    </div>
  );
}

export function Empty({
  art,
  title,
  text,
  action,
}: {
  art: ReactNode;
  title: string;
  text: string;
  action?: ReactNode;
}) {
  return (
    <div className={styles.empty}>
      {art}
      <h2 className={styles.emptyTitle}>{title}</h2>
      <p className={styles.emptyText}>{text}</p>
      {action && <div className={styles.emptyAction}>{action}</div>}
    </div>
  );
}

/** A lasting problem above what it belongs to, its one action under the text (artboard 14,
 *  states 3, 4 and 6): the design system's banner with the action moved down for a phone. */
export function Notice({
  tone,
  title,
  text,
  action,
}: {
  tone: Tone;
  title: string;
  text: string;
  action?: ReactNode;
}) {
  return (
    <div
      className={`ss-banner ss-tone-${tone} ${styles.notice}`}
      role={tone === "danger" ? "alert" : "status"}
    >
      <div className="ss-tone-icon" aria-hidden>
        <Icon name={TONE_ICON[tone]} size={18} strokeWidth={2} />
      </div>
      <div className={`ss-banner-body ${styles.noticeBody}`}>
        <p className="ss-banner-title">{title}</p>
        <p className="ss-banner-desc">{text}</p>
        {action && <div className={styles.noticeActions}>{action}</div>}
      </div>
    </div>
  );
}
