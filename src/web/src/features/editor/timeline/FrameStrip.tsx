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
  slide,
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

/** How far a press must travel before it is a drag and not a click */
const SLOP = 3;

/** A press on the strip: what it started on decides what a drag does.
 *  edge: an I/O handle · segment: a segment's body, dragged whole · empty: a range of frames
 *  for «Сделать отрезком» · playhead: the playhead, scrubbed. */
type Gesture =
  | { kind: "edge"; edge: Edge }
  | { kind: "segment"; x: number; frame: number; moving: boolean }
  | { kind: "empty"; x: number; frame: number; moving: boolean }
  | { kind: "playhead" };

/** The selected segment while it is dragged, drawn from here until the pointer lets go. */
interface Draft {
  /** The edge being dragged, or none when the whole segment moves */
  edge?: Edge;
  moved: Moved;
  wanted: number;
}

/** The frame strip of artboard 3b: the whole video at «Обзор», one cell per frame at «Кадры»,
 *  the segments over it, the I and O handles of the selected one and the playhead. A dragged
 *  handle or segment is drawn from local state and lands in the scene as one edit when
 *  released; meanwhile the canvas peeks at the frame under it and the playhead stays put. */
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
  const [drag, setDrag] = useState<Draft | null>(null);
  const wrapRef = useRef<HTMLDivElement>(null);

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
  const pushed: { edge: Edge; live: boolean } | undefined =
    drag?.edge && drag.moved.limited
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
  const peek = (frame: number) => useEditor.getState().setPeek(timeOf(frame, fps));
  const toFrame = (frame: number) => useEditor.getState().setTime(timeOf(frame, fps));
  const select = (from: number, to: number) =>
    useEditor.getState().setSelection(orderedSelection(from, to));

  const dragEdge = (edge: Edge, clientX: number) => {
    const boundary = Math.round(framesAt(clientX));
    const wanted = edge === "in" ? boundary : boundary - 1;
    const moved = moveEdge(segment, edge, wanted, bounds);
    setDrag({ edge, moved, wanted });
    peek(edge === "in" ? moved.inFrame : moved.outFrame);
  };

  const dragSegment = (from: number, clientX: number) => {
    const moved = { ...slide(segment, frameUnder(clientX) - from, total), limited: false };
    setDrag({ moved, wanted: moved.inFrame });
    peek(moved.inFrame);
  };

  const onPointerDown = (e: PointerEvent<HTMLDivElement>) => {
    if (e.button !== 0 || px === 0) return;
    const state = useEditor.getState();
    state.setPlaying(false);
    e.currentTarget.setPointerCapture(e.pointerId);
    const target = e.target as HTMLElement;
    const frame = frameUnder(e.clientX);

    const edge = target.closest<HTMLElement>("[data-edge]")?.dataset.edge as Edge | undefined;
    if (edge) {
      gesture.current = { kind: "edge", edge };
      dragEdge(edge, e.clientX);
      return;
    }
    state.setStop(undefined);
    if (target.closest("[data-playhead]")) {
      gesture.current = { kind: "playhead" };
      toFrame(frame);
      return;
    }
    const other = target.closest<HTMLElement>("[data-segment]")?.dataset.segment;
    if (other) {
      if (other !== segment.id) state.select(other);
      gesture.current = { kind: "segment", x: e.clientX, frame, moving: false };
      return;
    }
    // Shift+click extends the selection from the last click, in both scales.
    if (e.shiftKey && anchor.current !== undefined) {
      select(anchor.current, frame);
      gesture.current = { kind: "empty", x: e.clientX, frame: anchor.current, moving: true };
      return;
    }
    gesture.current = { kind: "empty", x: e.clientX, frame, moving: false };
  };

  const onPointerMove = (e: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;
    if (!current) return;
    if ((current.kind === "segment" || current.kind === "empty") && !current.moving) {
      if (Math.abs(e.clientX - current.x) < SLOP) return;
      current.moving = true;
    }
    // Near the ends of a zoomed strip, the strip scrolls under the pointer.
    const scroller = scrollerRef.current;
    if (scroller) {
      const rect = scroller.getBoundingClientRect();
      if (e.clientX < rect.left + 24) scroller.scrollLeft -= 16;
      else if (e.clientX > rect.right - 24) scroller.scrollLeft += 16;
    }
    if (current.kind === "edge") dragEdge(current.edge, e.clientX);
    else if (current.kind === "segment") dragSegment(current.frame, e.clientX);
    else if (current.kind === "empty") {
      anchor.current = current.frame;
      select(current.frame, frameUnder(e.clientX));
    } else toFrame(frameUnder(e.clientX));
  };

  const onPointerUp = (e: PointerEvent<HTMLDivElement>) => {
    const current = gesture.current;
    gesture.current = null;
    const state = useEditor.getState();
    state.setPeek(undefined);
    if (!current) return;
    // A click without a drag puts the playhead on the frame; at «Кадры» it also picks the frame.
    if ((current.kind === "segment" || current.kind === "empty") && !current.moving) {
      toFrame(current.frame);
      anchor.current = current.frame;
      state.setSelection(frames ? { from: current.frame, to: current.frame } : undefined);
      return;
    }
    if (!drag || e.type === "pointercancel") {
      setDrag(null);
      return;
    }
    setEdges(segment, drag.moved);
    if (drag.edge && drag.moved.limited) {
      state.setStop({ segmentId: segment.id, edge: drag.edge, wanted: drag.wanted });
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
    const { setStop, setPlaying } = useEditor.getState();
    setPlaying(false);
    peek(edge === "in" ? moved.inFrame : moved.outFrame);
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
      onBlur={() => useEditor.getState().setPeek(undefined)}
    />
  );

  const ranged = selection !== undefined && selection.to > selection.from;
  const toScreen = (x: number) => PAD + x - view.scroll;
  const lift = wrapRef.current?.offsetTop ?? 0;
  const tagEdge = drag ? (drag.edge ?? "in") : undefined;

  return (
    <div
      ref={wrapRef}
      className={styles.wrap}
      style={{ "--thumbs": `${thumbsH}px` } as CSSProperties}
    >
      <div
        ref={scrollerRef}
        className={styles.scroller}
        onScroll={(e) => {
          const scroll = e.currentTarget.scrollLeft;
          setView((v) => ({ ...v, scroll }));
        }}
      >
        <div
          className={styles.content}
          style={{ width: content }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
        >
          <Ruler px={px} fps={fps} total={total} first={first} last={last} />
          <div
            ref={trackRef}
            role="group"
            aria-label={t("editor.frames")}
            className={styles.track}
            style={{ height: frames ? CELL_H + 4 + 16 : TILE_H }}
          >
            {cells}
            {!frames && ranged && (
              <div
                className={styles.range}
                style={{
                  left: selection.from * px,
                  width: (selection.to - selection.from + 1) * px,
                }}
              />
            )}
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
              data-segment={segment.id}
              className={cx(styles.trim, drag && !drag.edge && styles.moving)}
              style={{ left: shown.inFrame * px, width: (shown.outFrame - shown.inFrame + 1) * px }}
            />
            {handle("in")}
            {handle("out")}
            {tagEdge && (
              <span
                className={styles.tag}
                style={{ left: tagEdge === "in" ? shown.inFrame * px : (shown.outFrame + 1) * px }}
              >
                {t("editor.frameN", { n: tagEdge === "in" ? shown.inFrame : shown.outFrame })}
              </span>
            )}
            <Playhead px={px} fps={fps} frames={frames} scroller={scrollerRef} />
          </div>
        </div>
      </div>

      {pushed && maxDuration !== undefined && (
        <Floating x={toScreen(edgeX(pushed.edge))} width={view.width + 2 * PAD} lift={lift}>
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
            <Button
              size="sm"
              variant="ghost"
              className={styles.tonal}
              disabled={pushed.live}
              onClick={() => fitStop(info)}
            >
              {t("editor.limit.fit")}
            </Button>
          </div>
        </Floating>
      )}

      {ranged && !drag && !pushed && (
        <Floating
          x={toScreen(((selection.from + selection.to + 1) / 2) * px)}
          width={view.width + 2 * PAD}
          lift={lift}
        >
          <div className={styles.panel} role="status">
            <span>
              {t(`editor.selection.${plural(selection.to - selection.from + 1)}`, {
                n: selection.to - selection.from + 1,
              })}
            </span>
            <Button
              size="sm"
              variant="ghost"
              icon="plus"
              className={styles.tonal}
              onClick={() => addSegment(info)}
            >
              {t("editor.selection.make")}
            </Button>
          </div>
        </Floating>
      )}
    </div>
  );
}

/** Floats above the whole timeline card with a caret down to `x`, the handle or the selection;
 *  kept inside the strip's width. `lift` is how far the strip sits below the card's top. */
function Floating({
  x,
  width,
  lift,
  children,
}: {
  x: number;
  width: number;
  lift: number;
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
  const caret = clamp(x - left, 24, Math.max(24, size - 24));
  return (
    <div
      ref={ref}
      className={styles.floating}
      style={{ left, bottom: `calc(100% + ${lift + 14}px)` }}
    >
      {children}
      <span className={styles.caret} style={{ left: caret }} aria-hidden />
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
  // The ruler scrubs the playhead, like the playhead itself.
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
    <div data-playhead className={styles.ruler} aria-hidden>
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

  return <div data-playhead className={styles.playhead} style={{ left: x }} aria-hidden />;
}
