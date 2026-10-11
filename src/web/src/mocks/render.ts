import type { Segment, SourceInfo } from "../shared/api/types";
import { outputOf, sourceFps, stickerDuration } from "./encoder";

/* The demo pipeline draws a real .webm in the browser: the demo video's frames (a tint per
   second and the frame's number, as on the timeline's strip) cropped or stretched as the
   segment says, played at its speed and loop, recorded in VP9 by MediaRecorder. So the exact
   preview and «Сравнить» play a real file, and the export downloads one that can be dropped
   back on the window to check it. */

const TINTS = ["#ffb38a", "#f7a8d8", "#b9a6ff", "#8fb8ff"];

export interface RenderTask {
  source: SourceInfo;
  segment: Segment;
  fps: number;
  side: number;
  /** The rate MediaRecorder aims at, so the file stays under the limit */
  bitsPerSecond: number;
}

export type Renderer = (task: RenderTask, onProgress: (fraction: number) => void) => Promise<Blob>;

function sourceFrame(
  ctx: CanvasRenderingContext2D,
  frame: number,
  source: SourceInfo,
  fps: number,
) {
  const second = Math.floor(frame / fps);
  const gradient = ctx.createLinearGradient(0, 0, source.width, source.height);
  gradient.addColorStop(0, TINTS[second % 4] ?? "#ccc");
  gradient.addColorStop(1, TINTS[(second + 1) % 4] ?? "#ccc");
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, source.width, source.height);
  ctx.fillStyle = "rgba(31, 32, 36, 0.7)";
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.font = `600 ${Math.round(source.height * 0.32)}px Manrope, sans-serif`;
  ctx.fillText(String(frame), source.width / 2, source.height / 2);
}

/** A tile of film grain. Flat gradients cost VP9 next to nothing; with grain the encoder
 *  spends about the rate it is given, so the file weighs what the forecast said. */
function grain(): HTMLCanvasElement {
  const tile = document.createElement("canvas");
  tile.width = 128;
  tile.height = 128;
  const ctx = tile.getContext("2d");
  if (!ctx) return tile;
  const image = ctx.createImageData(128, 128);
  for (let i = 0; i < image.data.length; i += 4) {
    const v = Math.random() * 255;
    image.data[i] = v;
    image.data[i + 1] = v;
    image.data[i + 2] = v;
    image.data[i + 3] = 255;
  }
  ctx.putImageData(image, 0, 0);
  return tile;
}

/** The source frame shown at output frame `k`: speed, and ping-pong's way back fitted into the
 *  same length (see stickerDuration). */
function frameAt(k: number, task: RenderTask): number {
  const { segment } = task;
  const span = segment.outFrame - segment.inFrame + 1;
  const pingpong = segment.loop === "pingpong" && span > 1;
  const pace = (pingpong ? 2 : 1) * segment.speed;
  const step = Math.floor((k / task.fps) * sourceFps(task.source) * pace);
  if (!pingpong) return segment.inFrame + (step % span);
  const cycle = 2 * span - 2;
  const at = step % cycle;
  return segment.inFrame + (at < span ? at : cycle - at);
}

export const browserRenderer: Renderer = (task, onProgress) =>
  new Promise((resolve, reject) => {
    const { source, segment } = task;
    const out = outputOf(segment, task.side);
    const canvas = document.createElement("canvas");
    canvas.width = out.width;
    canvas.height = out.height;
    const raw = document.createElement("canvas");
    raw.width = source.width;
    raw.height = source.height;
    const ctx = canvas.getContext("2d");
    const rawCtx = raw.getContext("2d");
    if (!ctx || !rawCtx) {
      reject(new Error("No 2D canvas"));
      return;
    }
    const stream = canvas.captureStream(0);
    const track = stream.getVideoTracks()[0] as CanvasCaptureMediaStreamTrack | undefined;
    const recorder = new MediaRecorder(stream, {
      mimeType: MediaRecorder.isTypeSupported("video/webm;codecs=vp9")
        ? "video/webm;codecs=vp9"
        : "video/webm",
      videoBitsPerSecond: task.bitsPerSecond,
    });
    const chunks: Blob[] = [];
    recorder.ondataavailable = (e) => {
      if (e.data.size > 0) chunks.push(e.data);
    };
    recorder.onstop = () => resolve(new Blob(chunks, { type: "video/webm" }));
    recorder.onerror = () => reject(new Error("MediaRecorder failed"));

    const noise = ctx.createPattern(grain(), "repeat");
    const fps = sourceFps(source);
    const count = Math.max(1, Math.round(stickerDuration(segment, fps) * task.fps));
    let k = 0;
    const draw = () => {
      if (k >= count) {
        recorder.stop();
        return;
      }
      sourceFrame(rawCtx, frameAt(k, task), source, fps);
      ctx.clearRect(0, 0, out.width, out.height);
      if (segment.frame.mode === "crop") {
        const { x, y, width, height } = segment.frame.rect;
        ctx.drawImage(raw, x, y, width, height, 0, 0, out.width, out.height);
      } else {
        ctx.drawImage(raw, 0, 0, out.width, out.height);
      }
      if (noise) {
        // A new offset every frame: the grain moves, as in a filmed video.
        ctx.save();
        ctx.globalAlpha = 0.07;
        ctx.translate(Math.random() * -128, Math.random() * -128);
        ctx.fillStyle = noise;
        ctx.fillRect(0, 0, out.width + 128, out.height + 128);
        ctx.restore();
      }
      track?.requestFrame();
      k += 1;
      onProgress(k / count);
      setTimeout(draw, 1000 / task.fps);
    };
    recorder.start();
    draw();
  });

/** The renderer when the page can record video; unit tests run without one. */
export function availableRenderer(): Renderer | undefined {
  const can =
    typeof document !== "undefined" &&
    typeof MediaRecorder !== "undefined" &&
    typeof HTMLCanvasElement.prototype.captureStream === "function";
  return can ? browserRenderer : undefined;
}
