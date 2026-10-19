import appIcon from "../../assets/app-icon.png";
import { Button, Icon } from "@stickersnap/ds";
import { openFeedback } from "../feedback/openFeedback";
import { useAppInfo } from "../../shared/api/queries";
import type { AppInfo } from "../../shared/api/types";
import { notify } from "../../shared/notify";
import { overlays } from "../../shared/overlays";
import { t } from "../../shared/strings/ru";
import { LicensesDialog } from "./LicensesDialog";
import { ffmpegText, versionsLine } from "./model";
import { SettingsSection } from "./Section";
import styles from "./sections.module.css";

async function copyVersions(app: AppInfo) {
  try {
    await navigator.clipboard.writeText(versionsLine(app, navigator.userAgent));
    notify.toast({
      tone: "success",
      title: t("settings.about.copied"),
      description: t("settings.about.copiedText"),
      key: "settings-versions",
    });
  } catch {
    notify.toast({
      tone: "warning",
      title: t("settings.about.copyFailed"),
      key: "settings-versions",
    });
  }
}

/** «О программе»: versions of the app and FFmpeg (GET /api/v1/app), the open-source
 *  licenses and «Написать отзыв…». */
export function AboutSection() {
  const app = useAppInfo().data;

  return (
    <SettingsSection id="about" title={t("settings.about")}>
      <div className={styles.about}>
        <img className={styles.logo} src={appIcon} width={56} height={56} alt="" />
        <div className={styles.aboutBody}>
          <span className={styles.accountName}>StickerSnap</span>
          <span className={`${styles.muted} ${styles.mono}`}>
            {app
              ? t("settings.about.versions", {
                  app: app.appVersion,
                  ffmpeg: ffmpegText(app.ffmpeg),
                })
              : "…"}
          </span>
        </div>
        <div className={styles.actions}>
          <Button disabled={!app} onClick={() => app && void copyVersions(app)}>
            {t("settings.about.copy")}
          </Button>
          <Button
            onClick={() => overlays.openWindow((close) => <LicensesDialog onClose={close} />)}
          >
            {t("settings.about.licenses")}
          </Button>
        </div>
      </div>
      {/* The second way into «Написать отзыв», for those who look in the settings (artboard 19) */}
      <div className={styles.account}>
        <span className={styles.accountIcon} aria-hidden>
          <Icon name="feedback" size={20} />
        </span>
        <div className={styles.accountBody}>
          <span className={styles.feedbackTitle}>{t("settings.about.feedback")}</span>
          <span className={styles.muted}>{t("settings.about.feedbackText")}</span>
        </div>
        <Button variant="primary" onClick={openFeedback}>
          {t("settings.about.feedbackOpen")}
        </Button>
      </div>
      <p className={styles.note}>{t("settings.about.text")}</p>
    </SettingsSection>
  );
}
