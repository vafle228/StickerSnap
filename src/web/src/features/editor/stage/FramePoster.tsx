import type { CSSProperties } from "react";
import { mediaSrc } from "../../../shared/api/client";
import { useEditor } from "../model/store";
import { frameAt } from "../model/timeline";

/** Tallest poster frame asked of the server: enough for the canvas, light to make. */
const MAX_HEIGHT = 720;

interface FramePosterProps {
  projectId: string;
  /** Source frames */
  total: number;
  height: number;
  fps: number;
  className?: string;
  style: CSSProperties;
}

/** The frame under the playhead (or a dragged handle) from the frames endpoint, standing in for
 *  the video while it cannot play: before its first frame arrives, or in the demo data, which
 *  has no video. The crop and the stretch are then still seen on a real picture. */
export function FramePoster({ projectId, total, height, fps, className, style }: FramePosterProps) {
  const frame = useEditor((s) => Math.min(total - 1, frameAt(s.peek ?? s.time, fps)));
  const h = Math.min(height, MAX_HEIGHT);
  return (
    <img
      className={className}
      style={style}
      alt=""
      src={mediaSrc(`/api/v1/projects/${projectId}/frames?from=${frame}&count=1&height=${h}`)}
    />
  );
}
