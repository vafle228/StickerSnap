import { Button, Dialog } from "../../ds";
import { platform } from "../../shared/platform";
import { LICENSES } from "./model";
import { t } from "../../shared/strings/ru";
import styles from "./sections.module.css";

/** «Лицензии…»: the one window a click on the scrim closes, there is nothing to lose (artboard 12). */
export function LicensesDialog({ onClose }: { onClose: () => void }) {
  return (
    <Dialog
      title={t("settings.about.licensesTitle")}
      text={t("settings.about.licensesText")}
      dismissible
      onClose={onClose}
      actions={
        <Button variant="secondary" data-autofocus onClick={onClose}>
          {t("common.close")}
        </Button>
      }
    >
      <ul className={styles.licenses}>
        {LICENSES.map((item) => (
          <li key={item.name} className={styles.license}>
            <a
              href={item.url}
              onClick={(e) => {
                e.preventDefault();
                platform.openExternal(item.url);
              }}
            >
              {item.name}
            </a>
            <span className={styles.muted}>{item.license}</span>
          </li>
        ))}
      </ul>
    </Dialog>
  );
}
