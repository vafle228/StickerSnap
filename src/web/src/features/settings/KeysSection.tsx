import { Kbd } from "../../ds";
import { t } from "../../shared/strings/ru";
import { hotkeysByScope } from "./model";
import { SettingsSection } from "./Section";
import styles from "./sections.module.css";

/** «Горячие клавиши» (R16): read-only, built from the hotkey registry, so a new key shows up
 *  here by itself. Own key bindings are P2. */
export function KeysSection() {
  return (
    <SettingsSection id="keys" title={t("settings.keys")} text={t("settings.keys.text")}>
      {hotkeysByScope().map(({ scope, keys }) => (
        <div key={scope} className={styles.group}>
          <h3 className={styles.eyebrow}>{t(`hotkey.scope.${scope}`)}</h3>
          <dl className={styles.keys}>
            {keys.map((key) => (
              <div key={key.label} className={styles.key}>
                <dt>{t(key.label)}</dt>
                <dd>
                  <Kbd keys={[...key.keys]} />
                </dd>
              </div>
            ))}
          </dl>
        </div>
      ))}
    </SettingsSection>
  );
}
