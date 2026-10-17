import { useMemo, useRef } from "react";
import { Card, Icon, SectionNav, type IconName } from "@stickersnap/ds";
import { useAccount, useAppInfo } from "../../shared/api/queries";
import { t, type StringKey } from "../../shared/strings/ru";
import { AboutSection } from "./AboutSection";
import { FoldersSection } from "./FoldersSection";
import { KeysSection } from "./KeysSection";
import { LookSection } from "./LookSection";
import type { SectionId } from "./Section";
import styles from "./SettingsScreen.module.css";
import { TelegramSection } from "./TelegramSection";
import { useActiveSection } from "./useActiveSection";

const SECTIONS: readonly { id: SectionId; label: StringKey; icon: IconName }[] = [
  { id: "bot", label: "settings.telegram", icon: "send" },
  { id: "look", label: "settings.look", icon: "layers" },
  { id: "folders", label: "settings.folders", icon: "folder" },
  { id: "keys", label: "settings.keys", icon: "keyboard" },
  { id: "about", label: "settings.about", icon: "info" },
];

/** Artboard 5. One screen: the table of contents on the left, every section in one scroll on
 *  the right. Changes apply at once (PATCH /settings), there is no «Сохранить». */
export function SettingsScreen() {
  const scroller = useRef<HTMLDivElement>(null);
  // «Папки» exist only in the desktop app: the web version downloads the .webm and keeps
  // projects on the server, where the user has no folders to pick or open.
  const desktop = useAppInfo().data?.mode === "desktop";
  const sections = useMemo(() => SECTIONS.filter((s) => desktop || s.id !== "folders"), [desktop]);
  const ids = useMemo(() => sections.map((s) => s.id), [sections]);
  const [active, setActive] = useActiveSection(scroller, ids);
  const linked = useAccount().data?.state === "linked";

  const items = sections.map(({ id, label, icon }) => ({
    value: id,
    label: (
      <>
        <Icon name={icon} size={18} />
        <span className={styles.tocLabel}>{t(label)}</span>
        {id === "bot" && linked && (
          <span className={styles.dot} role="img" aria-label={t("settings.bot.linkedDot")} />
        )}
      </>
    ),
  }));

  return (
    <div className={`${styles.screen} ss-section`}>
      <Card className={styles.toc}>
        <h1 className={styles.title}>{t("nav.settings")}</h1>
        <SectionNav<SectionId>
          className={styles.nav}
          label={t("settings.toc")}
          items={items}
          value={active}
          onChange={setActive}
        />
      </Card>
      <div ref={scroller} className={styles.content}>
        <TelegramSection />
        <LookSection />
        {desktop && <FoldersSection />}
        <KeysSection />
        <AboutSection />
      </div>
    </div>
  );
}
