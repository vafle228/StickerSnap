import type { Crop, Stretch } from "../../../shared/api/types";
import { sourceStretch, squashStretch, type Size } from "./frame";

/* The math of the frame on the canvas (CropFrame): moving and resizing the crop rect in source
   pixels, and the stretch box the source is squeezed into. Everything stays inside the source
   and lands in whole pixels; a drag that tries to turn the frame inside out stops at the minimum. */

export type Rect = Crop["rect"];
export type Corner = "nw" | "ne" | "sw" | "se";
export type Side = "n" | "e" | "s" | "w";

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

/** The proportion a crop preset holds while its corners are dragged; «Свободно» holds none. */
export function presetAspect(preset: Crop["preset"], source: Size): number | undefined {
  if (preset === "square") return 1;
  if (preset === "source") return source.width / source.height;
  return undefined;
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
 *  the rect keeps it. `min` is the shortest
 *  side in source pixels. */
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

/** Where the source is squeezed to in a stretch: the aspect as big as fits into the source's
 *  place, centered. Shrinking one side leaves the other where it was. */
export function stretchBox(aspect: Stretch["aspect"], source: Size): Rect {
  const scale = Math.min(source.width / aspect.width, source.height / aspect.height);
  const width = aspect.width * scale;
  const height = aspect.height * scale;
  return { x: (source.width - width) / 2, y: (source.height - height) / 2, width, height };
}

/** The stretch box with one side dragged by `delta` source pixels outwards; the box grows
 *  both ways from its center, so the dragged side stays under the pointer. */
export function resizeStretch(box: Rect, side: Side, delta: number, source: Size, min: number) {
  const across = side === "e" || side === "w";
  const room = across ? source.width : source.height;
  const size = clamp((across ? box.width : box.height) + 2 * delta, Math.min(min, room), room);
  const width = across ? size : box.width;
  const height = across ? box.height : size;
  return { x: (source.width - width) / 2, y: (source.height - height) / 2, width, height };
}

/** The stretch a dragged box stands for: its proportion, the preset it happens to match. */
export function stretchOf(box: Pick<Rect, "width" | "height">, source: Size): Stretch {
  const aspect = { width: Math.round(box.width), height: Math.round(box.height) };
  if (aspect.width === aspect.height) return squashStretch();
  if (likeSource(aspect, source)) return sourceStretch(source);
  return { mode: "stretch", preset: "free", aspect };
}
