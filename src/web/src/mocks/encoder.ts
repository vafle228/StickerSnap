import type {
  Concession,
  Estimate,
  LadderStep,
  PresetLimits,
  Segment,
  SourceInfo,
} from "../shared/api/types";

/* A stand-in for the VP9 encoder of the render pipeline: how many bytes a sticker takes at a
   given fps, CRF and side, and the fallback ladder of the plan («Рендер-пайплайн»): fps first,
   then CRF, then the side. The forecast and the export use the same numbers, so «≈ 330 КБ» in
   the inspector and the ladder after export agree, as they will with the real pipeline. */

export interface Attempt {
  fps: number;
  crf: number;
  /** The longer side of the output */
  side: number;
}

export interface Rung extends Attempt {
  bytes: number;
  concessions: Concession[];
}

const BASE_CRF = 31;
const STEP_CRF = 37;
const HEADER = 4096;

/** The busier the video, the more each frame costs; «Не влезет» plays a busy one. */
export function bytesPerFrame(scenario: string): number {
  return scenario === "too-big" ? 3700 : 2400;
}

export function sourceFps(source: Pick<SourceInfo, "fps">): number {
  return source.fps.num / source.fps.den;
}

/** Seconds the sticker plays, speed included, as the editor counts them (format.ts). How
 *  ping-pong meets the 3 s limit is an open question of the plan; the demo keeps the length
 *  and runs the way back inside it. */
export function stickerDuration(segment: Segment, fps: number): number {
  const frames = Math.max(1, segment.outFrame - segment.inFrame + 1);
  return frames / fps / segment.speed;
}

/** The output size: the longer side is the preset's, an emoji is a square (R3, R17). */
export function outputOf(segment: Segment, side: number): { width: number; height: number } {
  if (segment.preset === "emoji") return { width: side, height: side };
  const shape = segment.frame.mode === "crop" ? segment.frame.rect : segment.frame.aspect;
  const scale = side / Math.max(shape.width, shape.height);
  return {
    width: Math.max(1, Math.round(shape.width * scale)),
    height: Math.max(1, Math.round(shape.height * scale)),
  };
}

function bytesAt(
  attempt: Attempt,
  duration: number,
  perFrame: number,
  segment: Segment,
  limits: PresetLimits,
) {
  const { width, height } = outputOf(segment, attempt.side);
  const area = (width * height) / (limits.side * limits.side);
  const full = (limits.side * limits.side) / (512 * 512);
  const crf = 0.88 ** (attempt.crf - BASE_CRF);
  return Math.round(duration * attempt.fps * perFrame * area * full * crf + HEADER);
}

function concessionsOf(attempt: Attempt, first: Attempt): Concession[] {
  const list: Concession[] = [];
  if (attempt.fps !== first.fps) list.push({ kind: "fps", from: first.fps, to: attempt.fps });
  if (attempt.crf !== first.crf) list.push({ kind: "crf", from: first.crf, to: attempt.crf });
  if (attempt.side !== first.side) list.push({ kind: "scale", from: first.side, to: attempt.side });
  return list;
}

/** Every rung the pipeline would try, best quality first. */
export function ladder(
  source: SourceInfo,
  segment: Segment,
  limits: PresetLimits,
  perFrame: number,
): Rung[] {
  const fps = Math.min(Math.round(sourceFps(source)), limits.maxFps);
  const duration = stickerDuration(segment, sourceFps(source));
  const lower = [24, 20].filter((f) => f < fps);
  const last = lower.at(-1) ?? fps;
  const side = limits.side;
  const attempts: Attempt[] = [
    { fps, crf: BASE_CRF, side },
    ...lower.map((f) => ({ fps: f, crf: BASE_CRF, side })),
    { fps: last, crf: STEP_CRF, side },
    { fps: last, crf: STEP_CRF, side: Math.round(side * 0.875) },
    { fps: last, crf: STEP_CRF, side: Math.round(side * 0.75) },
  ];
  const first = attempts[0] as Attempt;
  return attempts.map((a) => ({
    ...a,
    bytes: bytesAt(a, duration, perFrame, segment, limits),
    concessions: concessionsOf(a, first),
  }));
}

/** The rungs tried until one fits, the fitting one last; none fits: the whole ladder and false. */
export function climb(rungs: Rung[], limit: number): { tried: Rung[]; fits: boolean } {
  const index = rungs.findIndex((r) => r.bytes <= limit);
  return index < 0
    ? { tried: rungs, fits: false }
    : { tried: rungs.slice(0, index + 1), fits: true };
}

export function ladderSteps(tried: Rung[]): LadderStep[] {
  return tried.map((r) => ({ label: r.concessions, bytes: r.bytes }));
}

/** POST /estimate: the first rung, what will likely be given up, and one-click fixes (3c G). */
export function estimateOf(
  source: SourceInfo,
  segment: Segment,
  limits: PresetLimits,
  perFrame: number,
): Estimate {
  const rungs = ladder(source, segment, limits, perFrame);
  const first = rungs[0] as Rung;
  const { tried, fits } = climb(rungs, limits.maxBytes);
  const fitting = !segment.autoFit ? first : fits ? tried.at(-1) : undefined;
  const srcFps = sourceFps(source);
  const out = outputOf(segment, first.side);
  const suggestions: Estimate["suggestions"] = [];
  if (first.bytes > limits.maxBytes) {
    const share = (limits.maxBytes - HEADER) / (first.bytes - HEADER);
    const frames = segment.outFrame - segment.inFrame + 1;
    const keep = Math.max(1, Math.floor(frames * share));
    suggestions.push({
      kind: "trim",
      outFrame: segment.inFrame + keep - 1,
      durationSec: stickerDuration({ ...segment, outFrame: segment.inFrame + keep - 1 }, srcFps),
    });
    const speed = Math.ceil((segment.speed / share) * 20) / 20;
    if (speed <= 4) suggestions.push({ kind: "speed", speed });
  }
  return {
    bytes: first.bytes,
    limitBytes: limits.maxBytes,
    fits: first.bytes <= limits.maxBytes,
    output: { ...out, fps: first.fps, durationSec: stickerDuration(segment, srcFps) },
    likelyConcessions: fitting ? fitting.concessions : (rungs.at(-1)?.concessions ?? []),
    suggestions,
  };
}
