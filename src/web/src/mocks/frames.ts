import { HttpResponse } from "msw";
import type { SourceInfo } from "../shared/api/types";
import { webp } from "./route";

// The prism tints, so demo frames look like the design system's placeholders.
const TINTS = ["#ffb38a", "#f7a8d8", "#b9a6ff", "#8fb8ff"];

/** A frame sprite as the contract describes it: `count` frames from `from`, in one row, each
 *  `height` tall and as wide as the source's proportion gives. Every second of the demo video
 *  has its own tint and every frame its number, so the strip shows which frame is where. */
export async function frameSprite(
  source: Pick<SourceInfo, "width" | "height" | "fps">,
  from: number,
  count: number,
  height: number,
): Promise<Response> {
  const width = Math.max(1, Math.round((height * source.width) / source.height));
  const canvas = new OffscreenCanvas(width * count, height);
  const ctx = canvas.getContext("2d");
  if (!ctx) return webp();
  const fps = source.fps.num / source.fps.den;
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.font = `600 ${Math.round(height * 0.32)}px Manrope, sans-serif`;
  for (let i = 0; i < count; i++) {
    const frame = from + i;
    const second = Math.floor(frame / fps);
    const gradient = ctx.createLinearGradient(i * width, 0, (i + 1) * width, height);
    gradient.addColorStop(0, TINTS[second % 4] ?? "#ccc");
    gradient.addColorStop(1, TINTS[(second + 1) % 4] ?? "#ccc");
    ctx.fillStyle = gradient;
    ctx.fillRect(i * width, 0, width, height);
    ctx.fillStyle = "rgba(31, 32, 36, 0.7)";
    ctx.fillText(String(frame), i * width + width / 2, height / 2);
  }
  const blob = await canvas.convertToBlob({ type: "image/webp" });
  return new HttpResponse(blob, { headers: { "Content-Type": "image/webp" } });
}
