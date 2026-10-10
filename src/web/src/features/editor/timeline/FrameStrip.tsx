import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
  type PointerEvent,
  type ReactNode,
  type RefObject,
} from "react";
import { Button, Icon } from "../../../ds";
import { cx } from "../../../ds/cx";
import { TONE_ICON } from "../../../ds/types";
import { mediaSrc } from "../../../shared/api/client";
import { plural, t } from "../../../shared/strings/ru";
import { num, seconds, speed as speedText, timecode } from "../format";
import { frameCount } from "../model/frame";
import { useEditor } from "../model/store";
import {
  clamp,
  FRAME_PX,
  frameAt,
  moveEdge,
  orderedSelection,
  pxPerFrame,
  tickStep,
  timeOf,
  type Edge,
  type Moved,
} from "../model/timeline";
import styles from "./FrameStrip.module.css";
import { addSegment, fitStop, setEdges, type TimelineInfo } from "./useTimeline";

/** Room at both ends of the strip for the handles and the playhead's dot */
const PAD = 8;
/** «Обзор» tiles and «Кадры» cells, artboards 3a and 3b C */
const TILE_H = 52;
const CELL_H = 48;
const GAP = 4;
/** Frames per sprite request in the «Кадры» scale */
const CHUNK = 30;

type Gesture = { kind: "edge"; edge: Edge } | { kind: "scrub" } | { kind: "select" };

interface EdgeDrag {
  edge: Edge;
  moved: Moved;
  wanted: number;
}

/** The frame strip of artboard 3b: the whole video at «Обзор», one cell per frame at «Кадры»,
 *  the segments over it, the I and O handles of the selected one and the playhead. A dragged
 *  handle is drawn from local state and lands in the scene as one edit when released. */
export function FrameStrip({ info }: { info: TimelineInfo }) {
  const { projectId, source, segments, segment, fps, total, bounds, maxDuration } = info;
  const zoom = useEditor((s) => s.timelineZoom);
  const selection = useEditor((s) => s.selection);
  const stop = useEditor((s) => s.stop);

  const scrollerRef = useRef<HTMLDivElement>(null);
  const trackRef = useRef<HTMLDivElement>(null);
  const gesture = useRef<Gesture | null>(null);
  const anchor = useRef<number | undefined>(undefined);
  const [view, setView] = useState({ width: 0, scroll: 0 });
  const [drag, setDrag] = useState<EdgeDrag | null>(null);

  useLayoutEffect(() => {
    const scroller = scrollerRef.current;
    if (!scroller) return undefined;
    const observer = new ResizeObserver(() =>
      setView((v) => ({ ...v, width: scroller.clientWidth - 2 * PAD })),
    );
    observer.observe(scroller);
    return () => observer.disconnect();
  }, []);

  const px = view.width > 0 ? pxPerFrame(zoom, view.width, total) : 0;
  const frames = px >= FRAME_PX;
  const content = total * px;
  const thumbsH = frames ? CELL_H : TILE_H;

  // A new scale keeps the playhead where it was on the screen.
  const lastPx = useRef(px);
  useLayoutEffect(() => {
    const scroller = scrollerRef.current;
    const before = lastPx.current;
    lastPx.current = px;
    if (!scroller || before === 0 || before === px) return;
    const at = useEditor.getState().time * fps;
    scroller.scrollLeft = at * px - (at * before - scroller.scrollLeft);
  }, [px, fps]);

  // The pushed edge's hint goes when a click lands anywhere else.
  const hintRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!stop) return undefined;
    const away = (e: globalThis.PointerEvent) => {
      if (!hintRef.current?.contains(e.target as Node)) useEditor.getState().setStop(undefined);
    };
    document.addEventListener("pointerdown", away);
    return () => document.removeEventListener("pointerdown", away);
  }, [stop]);

  const shown = drag ? drag.moved : segment;
  const pushed: { edge: Edge; live: boolean } | undefined = drag?.moved.limited
    ? { edge: drag.edge, live: true }
    : stop &&
        stop.segmentId === segment.id &&
        moveEdge(segment, stop.edge, stop.wanted, bounds).limited
      ? { edge: stop.edge, live: false }
      : undefined;
  const over = frameCount(segment) > bounds.max;
  const warnEdge = pushed?.edge ?? (over ? "out" : undefined);

  /** Frames from the strip's start to the pointer, fractional. */
  const framesAt = (clientX: number) => {
    const track = trackRef.current;
    return track && px > 0 ? (clientX - track.getBoundingClientRect().left) / px : 0;
  };
  const frameUnder = (clientX: number) => clamp(Math.floor(framesAt(clientX)), 0, total - 1);

  const dragEdge = (edge: Edge, clientX: number) => {
    const boundary = Math.round(framesAt(clientX));
    const wanted = edge === "in" ? boundary : boundary - 1;
    const moved = moveEdge(segment, edge, wanted, bounds);
    setDrag({ edge, moved, wanted });
    // The canvas shows the frame under the handle while it moves.
    useEditor.getState().setTime(timeOf(edge === "in" ? moved.inFrame : moved.outFrame, fps));
  };

  const pick = (clientX: number, extend: boolean) => {
    const { setTime, setSelection } = useEditor.getState();
    const frame = frameUnder(clientX);
    if (!extend || anchor.current === undefined) anchor.current = frame;
    setSelection(orderedSelection(anchor.current, frame));
    setTime(timeOf(frame, fps));
  };

  const scrub = (clientX: number) => useEditor.getState().setTime(timeOf(frameUnder(clientX), fps));

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0 || px === 0) return;
    const { setPlaying, select, setStop } = useEditor.getState();
    setPlaying(false);
    e.currentTarget.setPointerCapture(e.pointerId);
    const target = e.target as HTMLElement;
    const edge = target.closest<HTMLElement>("[data-edge]")?.dataset.edge as Edge | undefined;
    if (edge) {
      gesture.current = { kind: "edge", edge };
      dragEdge(edge, e.clientX);
      return;
    }
    const other = target.closest<HTMLElement>("[data-segment]")?.dataset.segment;
    if (other && other !== segment.id) select(other);
    setStop(undefined);
    if (frames) {
      gesture.current = { kind: "select" };
      pick(e.clientX, e.shiftKey);
    } else {
      gesture.current = { kind: "scrub" };
      scrub(e.clientX);
    }
  };

  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;
    if (!current) return;
    // Near the ends of a zoomed strip, the strip scrolls under the pointer.
    const scroller = scrollerRef.current;
    if (scroller) {
      const rect = scroller.getBoundingClientRect();
      if (e.clientX < rect.left + 24) scroller.scrollLeft -= 16;
      else if (e.clientX > rect.right - 24) scroller.scrollLeft += 16;
    }
    if (current.kind === "edge") dragEdge(current.edge, e.clientX);
    else if (current.kind === "select") pick(e.clientX, true);
    else scrub(e.clientX);
  };

  const onPointerUp = () => {
    const current = gesture.current;
    gesture.current = null;
    if (current?.kind !== "edge" || !drag) return;
    setEdges(segment, drag.moved);
    if (drag.moved.limited) {
      useEditor.getState().setStop({ segmentId: segment.id, edge: drag.edge, wanted: drag.wanted });
    }
    setDrag(null);
  };

  // ← → on a focused handle move that edge (Shift: ten frames); the playhead keys stay global.
  const onHandleKey = (edge: Edge) => (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
    e.preventDefault();
    e.stopPropagation();
    const step = (e.key === "ArrowLeft" ? -1 : 1) * (e.shiftKey ? 10 : 1);
    const from = edge === "in" ? segment.inFrame : segment.outFrame;
    const moved = moveEdge(segment, edge, from + step, bounds);
    setEdges(segment, moved);
    const { setStop, setTime, setPlaying } = useEditor.getState();
    setPlaying(false);
    setTime(timeOf(edge === "in" ? moved.inFrame : moved.outFrame, fps));
    setStop(moved.limited ? { segmentId: segment.id, edge, wanted: from + step } : undefined);
  };

  // Only what is on screen is drawn: a long video has thousands of frames.
  const first = px > 0 ? Math.max(0, Math.floor((view.scroll - PAD) / px) - 1) : 0;
  const last = px > 0 ? Math.min(total - 1, Math.ceil((view.scroll + view.width + PAD) / px)) : -1;
  const thumbs = source.status === "ok";
  const aspect = source.width / Math.max(1, source.height);
  const sprite = (from: number, count: number, height: number) =>
    `url("${mediaSrc(`/api/v1/projects/${projectId}/frames?from=${from}&count=${count}&height=${height}`)}")`;

  const cells = [];
  if (frames) {
    const cellW = px - GAP;
    const frameW = CELL_H * aspect;
    // Numbers are thinned out when four digits do not fit under a narrow cell.
    const digits = String(total - 1).length;
    const every = [1, 2, 5, 10].find((k) => k * cellW >= digits * 7) ?? 10;
    for (let f = first; f <= last; f++) {
      const chunk = Math.floor(f / CHUNK) * CHUNK;
      const count = Math.min(CHUNK, total - chunk);
      const style: CSSProperties = thumbs
        ? {
            backgroundImage: sprite(chunk, count, CELL_H * 2),
            backgroundSize: `${count * frameW}px ${CELL_H}px`,
            backgroundPosition: `${-((f - chunk) * frameW + (frameW - cellW) / 2)}px 0`,
          }
        : {};
      const picked = selection !== undefined && f >= selection.from && f <= selection.to;
      cells.push(
        <div key={f} className={styles.cell} style={{ left: f * px + GAP / 2, width: cellW }}>
          <span className={cx(styles.thumb, picked && styles.picked)} style={style} />
          <span className={styles.num}>{f % every === 0 ? f : ""}</span>
        </div>,
      );
    }
  } else if (px > 0) {
    const count = Math.max(1, Math.round(content / clamp(TILE_H * aspect, 40, 140)));
    const tileW = content / count;
    const from = Math.max(0, Math.floor((view.scroll - PAD) / tileW));
    const to = Math.min(count - 1, Math.ceil((view.scroll + view.width) / tileW));
    for (let i = from; i <= to; i++) {
      const frame = Math.min(total - 1, Math.floor(((i + 0.5) * tileW) / px));
      cells.push(
        <span
          key={i}
          className={cx(styles.tile, i % 2 === 1 && styles.alt)}
          style={{
            left: i * tileW + GAP / 2,
            width: tileW - GAP,
            backgroundImage: thumbs ? sprite(frame, 1, TILE_H * 2) : undefined,
          }}
        />,
      );
    }
  }

  const edgeX = (edge: Edge) => (edge === "in" ? shown.inFrame : shown.outFrame + 1) * px;
  const handle = (edge: Edge) => (
    <div
      data-edge={edge}
      role="slider"
      tabIndex={0}
      aria-label={t(edge === "in" ? "editor.handle.in" : "editor.handle.out")}
      aria-valuemin={0}
      aria-valuemax={total - 1}
      aria-valuenow={edge === "in" ? shown.inFrame : shown.outFrame}
      aria-valuetext={timecode(edgeX(edge) / px / fps)}
      className={cx(styles.handle, styles[edge], warnEdge === edge && styles.limit)}
      style={{ left: edgeX(edge) }}
      onKeyDown={onHandleKey(edge)}
    />
  );

  const showPanel = frames && !drag && selection !== undefined && selection.to > selection.from;
  const toScreen = (x: number) => PAD + x - view.scroll;

  return (
    <div className={styles.wrap} style={{ "--thumbs": `${thumbsH}px` } as CSSProperties}>
      <div
        ref={scrollerRef}
        className={styles.scroller}
        onScroll={(e) => {
          const scroll = e.currentTarget.scrollLeft;
          setView((v) => ({ ...v, scroll }));
        }}
      >
        <div className={styles.content} style={{ width: content }}>
          <Ruler px={px} fps={fps} total={total} first={first} last={last} />
          <div
            ref={trackRef}
            role="group"
            aria-label={t("editor.frames")}
            className={styles.track}
            style={{ height: frames ? CELL_H + 4 + 16 : TILE_H }}
            onPointerDown={onPointerDown}
            onPointerMove={onPointerMove}
            onPointerUp={onPointerUp}
            onPointerCancel={onPointerUp}
          >
            {cells}
            {segments
              .filter((s) => s.id !== segment.id)
              .map((s) => (
                <div
                  key={s.id}
                  data-segment={s.id}
                  className={styles.other}
                  style={{ left: s.inFrame * px, width: (s.outFrame - s.inFrame + 1) * px }}
                  title={s.name}
                >
                  <span className={styles.chip}>{s.name}</span>
                </div>
              ))}
            <div
              className={styles.trim}
              style={{ left: shown.inFrame * px, width: (shown.outFrame - shown.inFrame + 1) * px }}
            />
            {handle("in")}
            {handle("out")}
            <Playhead px={px} fps={fps} frames={frames} scroller={scrollerRef} />
          </div>
        </div>
      </div>

      {pushed && maxDuration !== undefined && (
        <Floating x={toScreen(edgeX(pushed.edge))} width={view.width + 2 * PAD}>
          <div
            ref={hintRef}
            className={`ss-toast ss-tone-warning ${styles.hint}`}
            role="alert"
            aria-live="assertive"
          >
            <span className="ss-tone-icon" aria-hidden>
              <Icon name={TONE_ICON.warning} size={18} strokeWidth={2} />
            </span>
            <span className={styles.hintText}>
              {t("editor.limit", { max: num(maxDuration, 2), speed: speedText(segment.speed) })}
            </span>
            <Button size="sm" disabled={pushed.live} onClick={() => fitStop(info)}>
              {t("editor.limit.fit")}
            </Button>
          </div>
        </Floating>
      )}

      {showPanel && !pushed && (
        <Floating
          x={toScreen(((selection.from + selection.to + 1) / 2) * px)}
          width={view.width + 2 * PAD}
          className={styles.panelSpot}
        >
          <div className={styles.panel} role="status">
            <span>
              {t(`editor.selection.${plural(selection.to - selection.from + 1)}`, {
                n: selection.to - selection.from + 1,
              })}
            </span>
            <Button size="sm" variant="ghost" icon="plus" onClick={() => addSegment(info)}>
              {t("editor.selection.make")}
            </Button>
          </div>
        </Floating>
      )}
    </div>
  );
}

/** A floating thing over the strip, centered on `x` but kept inside the strip's width. */
function Floating({
  x,
  width,
  className,
  children,
}: {
  x: number;
  width: number;
  className?: string;
  children: ReactNode;
}) {
  const ref = useRef<HTMLDivElement>(null);
  const [size, setSize] = useState(0);
  useLayoutEffect(() => {
    const box = ref.current;
    if (!box) return undefined;
    const observer = new ResizeObserver(() => setSize(box.offsetWidth));
    observer.observe(box);
    return () => observer.disconnect();
  }, []);
  const left = clamp(x - size / 2, 0, Math.max(0, width - size));
  return (
    <div ref={ref} className={cx(styles.floating, className)} style={{ left }}>
      {children}
    </div>
  );
}

/** Seconds over the frames: «0 с», then round steps, and the length at the end. */
function Ruler({
  px,
  fps,
  total,
  first,
  last,
}: {
  px: number;
  fps: number;
  total: number;
  first: number;
  last: number;
}) {
  if (px === 0) return <div className={styles.ruler} />;
  const step = tickStep(px * fps);
  const end = total * px;
  const ticks = [];
  for (let i = Math.ceil(first / fps / step); i * step * fps <= last + 1; i++) {
    const x = i * step * fps * px;
    if (i > 0 && x > end - 72) break;
    ticks.push(
      <span key={i} className={cx(styles.tick, i === 0 && styles.start)} style={{ left: x }}>
        {i === 0 ? t("editor.seconds", { value: 0 }) : seconds(i * step)}
      </span>,
    );
  }
  return (
    <div className={styles.ruler} aria-hidden>
      {ticks}
      <span className={cx(styles.tick, styles.end)} style={{ left: end }}>
        {t("editor.seconds", { value: seconds(Math.round((total / fps) * 10) / 10) })}
      </span>
    </div>
  );
}

/** The playhead follows the clock on its own, so the strip does not re-render every frame.
 *  When it leaves a zoomed strip, the strip scrolls after it. */
function Playhead({
  px,
  fps,
  frames,
  scroller,
}: {
  px: number;
  fps: number;
  frames: boolean;
  scroller: RefObject<HTMLDivElement | null>;
}) {
  const time = useEditor((s) => s.time);
  // In cells the playhead stands in the middle of its frame, as on artboard 3b C.
  const x = frames ? (frameAt(time, fps) + 0.5) * px : time * fps * px;

  useEffect(() => {
    const box = scroller.current;
    if (!box) return;
    const inner = box.clientWidth - 2 * PAD;
    if (x < box.scrollLeft || x > box.scrollLeft + inner) box.scrollLeft = x - inner * 0.1;
  }, [x, scroller]);

  return <div className={styles.playhead} style={{ left: x }} aria-hidden />;
}
