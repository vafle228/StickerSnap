import type { CSSProperties } from "react";
import type { PresetLimits, Segment } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import { frameInput, outputSize, type Size } from "../model/frame";
import styles from "./VideoStage.module.css";

interface OutputFrameProps {
  frame: Segment["frame"];
  preset: Segment["preset"];
  source: Size;
  limits?: Pick<PresetLimits, "side">;
  /** Display px per source px */
  scale: number;
  /** «Кроп» is on: the label with the sizes shows */
  labelled: boolean;
}

/** What goes into the sticker, drawn over the source: the crop frame with the rest dimmed, or
 *  the whole frame for a stretch. Read-only here: dragging it is CropFrame's job. */
export function OutputFrame({ frame, preset, source, limits, scale, labelled }: OutputFrameProps) {
  const style: CSSProperties =
    frame.mode === "crop"
      ? {
          left: frame.rect.x * scale,
          top: frame.rect.y * scale,
          width: frame.rect.width * scale,
          height: frame.rect.height * scale,
        }
      : { inset: 0 };
  const input = frameInput(frame, source);
  const output = limits && outputSize(frame, preset, limits);

  return (
    <div className={frame.mode === "crop" ? styles.crop : styles.stretch} style={style}>
      {labelled && output && (
        <span className={styles.frameLabel}>
          {t(frame.mode === "crop" ? "editor.frame.crop" : "editor.frame.stretch", {
            inW: input.width,
            inH: input.height,
            outW: output.width,
            outH: output.height,
          })}
        </span>
      )}
    </div>
  );
}
