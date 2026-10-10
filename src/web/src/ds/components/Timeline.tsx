import type { CSSProperties } from "react";
import { cx } from "../cx";

export interface TimelineProps {
  frames?: number;
  thumbs?: string[];
  /** 0–1 fractions of the strip */
  trimStart?: number;
  trimEnd?: number;
  playhead?: number;
  /** Playhead time code, «00:01.120» */
  time?: string;
  /** «выбрано 2,4 с из 3,6 с» */
  duration?: string;
  className?: string;
}

const TINTS = ["var(--tint-peach)", "var(--tint-pink)", "var(--tint-lilac)", "var(--tint-sky)"];
const PAD = 8;

/** Position inside the strip padding: fraction 0–1 of the frames area. */
const at = (fraction: number) => `calc(${PAD}px + (100% - ${2 * PAD}px) * ${fraction})`;

/** Frame strip with the trim frame (accent) and the playhead (peach, never the accent).
 *  Static look for now: dragging comes with the editor stage. */
export function Timeline({
  frames = 12,
  thumbs,
  trimStart = 0.17,
  trimEnd = 0.83,
  playhead = 0.4,
  time = "00:01.120",
  duration = "выбрано 2,4 с из 3,6 с",
  className,
}: TimelineProps) {
  const cells = Array.from({ length: frames }, (_, i) => {
    const from = i / frames;
    const out = from + 1 / frames <= trimStart || from >= trimEnd;
    const thumb = thumbs?.[i];
    const style: CSSProperties = thumb
      ? { backgroundImage: `url(${thumb})`, backgroundSize: "cover", backgroundPosition: "center" }
      : { background: `linear-gradient(160deg, ${TINTS[i % 4]}, ${TINTS[(i + 1) % 4]})` };
    return <div key={i} className={cx("ss-frame", out && "is-out")} style={style} />;
  });

  return (
    <div className={cx("ss-timeline", className)}>
      <div className="ss-timeline-head">
        <span className="ss-playtime">{time}</span>
        <span>{duration}</span>
      </div>
      <div className="ss-strip" role="group" aria-label="Таймлайн">
        {cells}
        <div
          className="ss-trim"
          style={{
            left: `calc(${at(trimStart)} - 4px)`,
            right: `calc(${at(1 - trimEnd)} - 4px)`,
          }}
        />
        <div className="ss-playhead" style={{ left: at(playhead) }} />
      </div>
    </div>
  );
}
