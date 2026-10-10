/** Scales of source pixels the zoom buttons step through; «По окну» sits between two of them. */
export const ZOOM_STEPS = [0.1, 0.25, 0.5, 0.75, 1, 1.5, 2, 3, 4] as const;
export const ZOOM_MIN = 0.1;
export const ZOOM_MAX = 4;

const EPS = 1e-6;

/** The step above the scale shown now (which may be the fit scale). */
export function zoomIn(scale: number): number {
  return ZOOM_STEPS.find((z) => z > scale + EPS) ?? ZOOM_MAX;
}

export function zoomOut(scale: number): number {
  return [...ZOOM_STEPS].reverse().find((z) => z < scale - EPS) ?? ZOOM_MIN;
}

/** The scale at which `content` fits into `box`. */
export function fitScale(
  content: { width: number; height: number },
  box: { width: number; height: number },
): number {
  if (content.width <= 0 || content.height <= 0) return 1;
  return Math.max(0, Math.min(box.width / content.width, box.height / content.height));
}
