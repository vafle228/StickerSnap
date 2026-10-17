import { Button, Card, Segmented, Slider } from "@stickersnap/ds";
import { useAppInfo } from "../../../shared/api/queries";
import type { Segment } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import { speed as speedText } from "../format";
import { fitSpeed, fpsOf, frameCount, playLimit, SPEED_MAX, SPEED_MIN } from "../model/frame";
import { editSegment, useEditor, useSegment } from "../model/store";
import styles from "./Inspector.module.css";

/* The slider runs on log2 of the speed, so 1× sits in the middle between 0,25× and 4×,
   and snaps to 0,05× steps. */
const toSlider = (speed: number) => Math.log2(speed);
const fromSlider = (value: number) =>
  Math.min(SPEED_MAX, Math.max(SPEED_MIN, Math.round(2 ** value * 20) / 20));
const shown = (value: number) => speedText(Math.round(2 ** value * 100) / 100);

/** «Скорость и цикл» (R4, R5). */
export function SpeedLoopCard() {
  const segment = useSegment();
  const source = useEditor((s) => s.source);
  const limits = useAppInfo().data?.limits;
  if (!segment || !source) return null;

  const fps = fpsOf(source);
  const maxDuration = limits && playLimit(limits[segment.preset], segment);
  const fit = maxDuration !== undefined ? fitSpeed(segment, fps, maxDuration) : undefined;
  // Even at 4× the segment runs past the limit: «Вписать» cannot help, the timeline must trim.
  const tooLong =
    maxDuration !== undefined && frameCount(segment) / fps / SPEED_MAX > maxDuration + 1e-9;

  return (
    <Card eyebrow={t("inspector.speedLoop")} className={styles.card}>
      <div>
        <Slider
          label={t("inspector.speed")}
          min={toSlider(SPEED_MIN)}
          max={toSlider(SPEED_MAX)}
          step={0.01}
          value={toSlider(segment.speed)}
          format={shown}
          // A drag of the slider is one step of history, not one per tick
          onChange={(value) =>
            editSegment((s) => void (s.speed = fromSlider(value)), { merge: "speed" })
          }
        />
        <div className={styles.scale} aria-hidden>
          <span>{speedText(SPEED_MIN)}</span>
          <span>{speedText(1)}</span>
          <span>{speedText(SPEED_MAX)}</span>
        </div>
      </div>
      <Button
        className={styles.wide}
        disabled={fit === undefined || tooLong || fit === segment.speed}
        reason={
          tooLong ? t("inspector.fitWholeReason", { speed: speedText(SPEED_MAX) }) : undefined
        }
        onClick={() => fit !== undefined && editSegment((s) => void (s.speed = fit))}
      >
        {t("inspector.fitWhole")}
      </Button>
      <Segmented<Segment["loop"]>
        label={t("inspector.loopMode")}
        className={styles.wide}
        items={[
          { value: "normal", label: t("inspector.loop.normal") },
          { value: "pingpong", label: t("inspector.loop.pingpong") },
        ]}
        value={segment.loop}
        onChange={(loop) => editSegment((s) => void (s.loop = loop))}
      />
    </Card>
  );
}
