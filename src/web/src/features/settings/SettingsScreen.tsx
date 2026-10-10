import { useRef } from "react";
import { Card, Icon, SectionNav, type IconName } from "../../ds";
import { useAccount } from "../../shared/api/queries";
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
const IDS = SECTIONS.map((s) => s.id);

/** Artboard 5. One screen: the table of contents on the left, every section in one scroll on
 *  the right. Changes apply at once (PATCH /settings), there is no «Сохранить». */
export function SettingsScreen() {
  const scroller = useRef<HTMLDivElement>(null);
  const [active, setActive] = useActiveSection(scroller, IDS);
  const linked = useAccount().data?.state === "linked";

  const items = SECTIONS.map(({ id, label, icon }) => ({
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
    <div className={styles.screen}>
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
        <FoldersSection />
        <KeysSection />
        <AboutSection />
      </div>
    </div>
  );
}
