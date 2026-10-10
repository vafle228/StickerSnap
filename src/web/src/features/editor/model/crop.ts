import type { Crop, Stretch } from "../../../shared/api/types";
import type { Size } from "./frame";

/* The math of the frame on the canvas (CropFrame): moving and resizing the crop rect in source
   pixels, and the stretch box the source is squeezed into. A crop stays inside the source; a
   stretch box sits on the source's center and may grow past it. Everything lands in whole
   pixels; a drag that tries to turn the frame inside out stops at the minimum. */

export type Rect = Crop["rect"];
export type Corner = "nw" | "ne" | "sw" | "se";
export type Side = "n" | "e" | "s" | "w";

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

/** The proportion a crop holds while its corners are dragged without Shift: the preset's, or
 *  for a free crop the one it has now. */
export function cropAspect(crop: Pick<Crop, "preset" | "rect">, source: Size): number {
  if (crop.preset === "square") return 1;
  if (crop.preset === "source") return source.width / source.height;
  return crop.rect.width / crop.rect.height;
}

/** Within a pixel of the source's proportion. */
function likeSource(size: Pick<Rect, "width" | "height">, source: Size) {
  return Math.abs(size.width * source.height - size.height * source.width) <= source.height;
}

/** The preset a rect matches after a free resize: a square or the source's proportion. */
export function cropPreset(rect: Pick<Rect, "width" | "height">, source: Size): Crop["preset"] {
  if (rect.width === rect.height) return "square";
  return likeSource(rect, source) ? "source" : "free";
}

/** The rect moved by (dx, dy) source pixels, kept inside the source. */
export function moveCrop(rect: Rect, dx: number, dy: number, source: Size): Rect {
  return {
    ...rect,
    x: Math.round(clamp(rect.x + dx, 0, source.width - rect.width)),
    y: Math.round(clamp(rect.y + dy, 0, source.height - rect.height)),
  };
}

/** The rect with `corner` dragged by (dx, dy) while the opposite corner stays. With an aspect
 *  the rect keeps it. `min` is the shortest side in source pixels. */
export function resizeCrop(
  rect: Rect,
  corner: Corner,
  dx: number,
  dy: number,
  source: Size,
  aspect: number | undefined,
  min: number,
): Rect {
  const east = corner.endsWith("e");
  const south = corner.startsWith("s");
  const anchorX = east ? rect.x : rect.x + rect.width;
  const anchorY = south ? rect.y : rect.y + rect.height;
  const maxW = east ? source.width - anchorX : anchorX;
  const maxH = south ? source.height - anchorY : anchorY;
  const wantW = rect.width + (east ? dx : -dx);
  const wantH = rect.height + (south ? dy : -dy);

  let width: number;
  let height: number;
  if (aspect === undefined) {
    width = clamp(Math.round(wantW), Math.min(min, maxW), maxW);
    height = clamp(Math.round(wantH), Math.min(min, maxH), maxH);
  } else {
    const limit = Math.min(maxW, maxH * aspect);
    const least = Math.min(Math.max(min, min * aspect), limit);
    // The axis the pointer went further along leads; the other follows the aspect.
    const byWidth = Math.abs(wantW - rect.width) >= Math.abs(wantH - rect.height) * aspect;
    width = Math.floor(clamp(byWidth ? wantW : wantH * aspect, least, limit));
    height = Math.min(Math.round(width / aspect), Math.floor(maxH));
  }
  return {
    x: Math.round(east ? anchorX : anchorX - width),
    y: Math.round(south ? anchorY : anchorY - height),
    width,
    height,
  };
}

/** How far a stretch box may grow past the source: four times its longer side. */
const STRETCH_MAX = 4;

/** Where the source is squeezed to in a stretch, in source pixels around the source's center:
 *  the aspect is the box itself («Как в источнике» is the source's own size). An aspect too
 *  small to be a size (a bare 1:1) is taken as a proportion and fitted into the source. */
export function stretchBox(aspect: Stretch["aspect"], source: Size): Rect {
  const scale =
    Math.max(aspect.width, aspect.height) < 16
      ? Math.min(source.width / aspect.width, source.height / aspect.height)
      : 1;
  const width = aspect.width * scale;
  const height = aspect.height * scale;
  return { x: (source.width - width) / 2, y: (source.height - height) / 2, width, height };
}

/** The stretch box with one side dragged by `delta` source pixels outwards; the box grows
 *  both ways from its center, so the dragged side stays under the pointer. */
export function resizeStretch(box: Rect, side: Side, delta: number, source: Size, min: number) {
  const across = side === "e" || side === "w";
  const most = STRETCH_MAX * Math.max(source.width, source.height);
  const size = clamp((across ? box.width : box.height) + 2 * delta, min, most);
  const width = across ? size : box.width;
  const height = across ? box.height : size;
  return { x: (source.width - width) / 2, y: (source.height - height) / 2, width, height };
}

/** The stretch a dragged box stands for: its size, and the preset its proportion matches. */
export function stretchOf(box: Pick<Rect, "width" | "height">, source: Size): Stretch {
  const aspect = { width: Math.round(box.width), height: Math.round(box.height) };
  const preset =
    aspect.width === aspect.height ? "squash" : likeSource(aspect, source) ? "source" : "free";
  return { mode: "stretch", preset, aspect };
}

/** Sizes typed in the inspector (the output's) as a box as big as the one there is now. */
export function stretchLike(size: Stretch["aspect"], now: Stretch["aspect"], source: Size) {
  const current = stretchBox(now, source);
  const scale = Math.max(current.width, current.height) / Math.max(size.width, size.height);
  return { width: Math.round(size.width * scale), height: Math.round(size.height * scale) };
}

/** What the canvas shows a frame through: the crop, or the stretch box. */
export function frameBox(frame: Crop | Stretch, source: Size): Rect {
  return frame.mode === "crop" ? frame.rect : stretchBox(frame.aspect, source);
}
