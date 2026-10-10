import { Button, Card } from "../../../ds";
import { t } from "../../../shared/strings/ru";
import { useSegment } from "../model/store";
import { ExportCard } from "./ExportCard";
import { FrameCard } from "./FrameCard";
import styles from "./Inspector.module.css";
import { SpeedLoopCard } from "./SpeedLoopCard";

/** Everything about one sticker at once, no tabs or wizards (artboard 3a). */
export function Inspector() {
  const segment = useSegment();
  // A project without segments is fine: there is just nothing to export yet.
  if (!segment) {
    return (
      <Card eyebrow={t("inspector.export")} className={styles.card}>
        <p className={styles.empty}>{t("inspector.noSegments")}</p>
        <Button className={styles.wide} disabled reason={t("inspector.noSegmentsReason")}>
          {t("inspector.exportWebm")}
        </Button>
      </Card>
    );
  }
  return (
    <>
      <FrameCard />
      <SpeedLoopCard />
      <ExportCard />
    </>
  );
}
