import type { Crop, PresetLimits, Segment, SourceInfo, Stretch } from "../../../shared/api/types";

/* The «Кадр» card's math (R3): what a preset does to the frame and what size comes out.
   The scene keeps the crop in source pixels and the stretch as an aspect; the output's
   longer side is always the preset's side (512 for a sticker, 100 for an emoji). */

export interface Size {
  width: number;
  height: number;
}

export function fpsOf(source: Pick<SourceInfo, "fps">): number {
  return source.fps.num / source.fps.den;
}

/** The biggest centered square: «Квадрат». In whole pixels, as the server takes a crop: an odd
 *  margin leaves the extra pixel on the right or at the bottom. */
export function squareCrop(source: Size): Crop {
  const side = Math.min(source.width, source.height);
  return {
    mode: "crop",
    preset: "square",
    rect: {
      x: Math.floor((source.width - side) / 2),
      y: Math.floor((source.height - side) / 2),
      width: side,
      height: side,
    },
  };
}

/** The whole frame: «Как в источнике». */
export function sourceCrop(source: Size): Crop {
  return {
    mode: "crop",
    preset: "source",
    rect: { x: 0, y: 0, width: source.width, height: source.height },
  };
}

export function sourceStretch(source: Size): Stretch {
  return {
    mode: "stretch",
    preset: "source",
    aspect: { width: source.width, height: source.height },
  };
}

/** «Сплющить в 1:1»: the longer side squeezed down to the shorter one. */
export function squashStretch(source: Size): Stretch {
  const side = Math.min(source.width, source.height);
  return { mode: "stretch", preset: "squash", aspect: { width: side, height: side } };
}

/** The frame a mode starts with when the user switches to it. */
export function defaultFrame(mode: "crop" | "stretch", source: Size): Segment["frame"] {
  return mode === "crop" ? squareCrop(source) : sourceStretch(source);
}

/** Longer side becomes `side`, the shorter one keeps the proportion. */
function fit(size: Size, side: number): Size {
  const scale = side / Math.max(size.width, size.height);
  return {
    width: Math.max(1, Math.round(size.width * scale)),
    height: Math.max(1, Math.round(size.height * scale)),
  };
}

/** The pixels of the source that end up in the sticker. */
export function frameInput(frame: Segment["frame"], source: Size): Size {
  return frame.mode === "crop"
    ? { width: Math.round(frame.rect.width), height: Math.round(frame.rect.height) }
    : { width: source.width, height: source.height };
}

/** The size of the rendered sticker. An emoji is always a full square (R17). */
export function outputSize(
  frame: Segment["frame"],
  preset: Segment["preset"],
  limits: Pick<PresetLimits, "side">,
): Size {
  if (preset === "emoji") return { width: limits.side, height: limits.side };
  const shape = frame.mode === "crop" ? frame.rect : frame.aspect;
  return fit(shape, limits.side);
}

/** Frames that play: from In to Out inclusive, minus the excluded ones (P1). */
export function frameCount(segment: Pick<Segment, "inFrame" | "outFrame" | "excludedFrames">) {
  return Math.max(0, segment.outFrame - segment.inFrame + 1 - segment.excludedFrames.length);
}

/** Seconds the sticker plays, speed included. */
export function segmentDuration(
  segment: Pick<Segment, "inFrame" | "outFrame" | "excludedFrames" | "speed">,
  fps: number,
): number {
  return frameCount(segment) / fps / segment.speed;
}

export const SPEED_MIN = 0.25;
export const SPEED_MAX = 4;

/** How long a segment may play: Telegram's 3 s, or the 10 s of a long sticker (R19). */
export function playLimit(
  limits: Pick<PresetLimits, "maxDurationSec" | "longMaxDurationSec">,
  segment: Pick<Segment, "longSticker"> | undefined,
): number {
  return segment?.longSticker ? limits.longMaxDurationSec : limits.maxDurationSec;
}

/** «Вписать целиком»: the slowest speed at which the whole segment fits the limit. */
export function fitSpeed(
  segment: Pick<Segment, "inFrame" | "outFrame" | "excludedFrames">,
  fps: number,
  maxDurationSec: number,
): number {
  const raw = frameCount(segment) / fps / maxDurationSec;
  // Rounded up, so the result never runs a hair over the limit.
  const speed = Math.ceil(raw * 100 - 1e-9) / 100;
  return Math.min(SPEED_MAX, Math.max(SPEED_MIN, speed));
}
