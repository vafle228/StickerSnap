import { http, HttpResponse } from "msw";
import type { Shape } from "./data";

/* The demo stickers' videos: a shape that bounces for a second and a half, drawn on a
   transparent canvas and recorded to .webm in the browser, once per shape and colour.
   Dev only, like the rest of mocks/. */

const SIDE = 256;
const DURATION_MS = 1500;
const cache = new Map<string, Promise<Blob>>();

function draw(context: CanvasRenderingContext2D, color: string, shape: Shape, t: number) {
  const bounce = Math.abs(Math.sin(t * Math.PI * 2));
  const size = SIDE * (0.5 + 0.08 * bounce);
  const x = (SIDE - size) / 2;
  const y = (SIDE - size) / 2 - SIDE * 0.08 * bounce;
  const radius = shape === "circle" ? size / 2 : shape === "square" ? size * 0.16 : size * 0.32;
  context.clearRect(0, 0, SIDE, SIDE);
  context.beginPath();
  context.roundRect(x, y, size, size, radius);
  context.fillStyle = color;
  context.fill();
  context.lineWidth = 6;
  context.strokeStyle = "#ffffff";
  context.stroke();
}

function record(color: string, shape: Shape): Promise<Blob> {
  const canvas = document.createElement("canvas");
  canvas.width = SIDE;
  canvas.height = SIDE;
  const context = canvas.getContext("2d");
  if (!context || typeof MediaRecorder === "undefined")
    return Promise.reject(new Error("no canvas"));
  const stream = canvas.captureStream(30);
  const type = MediaRecorder.isTypeSupported("video/webm;codecs=vp9")
    ? "video/webm;codecs=vp9"
    : "video/webm";
  const recorder = new MediaRecorder(stream, { mimeType: type });
  const chunks: Blob[] = [];
  recorder.ondataavailable = (e) => chunks.push(e.data);
  return new Promise((resolve) => {
    recorder.onstop = () => resolve(new Blob(chunks, { type: "video/webm" }));
    const start = performance.now();
    const frame = () => {
      const elapsed = performance.now() - start;
      draw(context, color, shape, (elapsed % DURATION_MS) / DURATION_MS);
      if (elapsed < DURATION_MS) requestAnimationFrame(frame);
      else recorder.stop();
    };
    recorder.start();
    frame();
  });
}

export const mediaHandlers = [
  http.get("/mock-media/missing.webm", () => new HttpResponse(null, { status: 404 })),
  http.get("/mock-media/:shape/:color", async ({ params }) => {
    const shape = String(params.shape) as Shape;
    const color = `#${String(params.color).replace(".webm", "")}`;
    const key = `${shape}${color}`;
    let video = cache.get(key);
    if (!video) {
      video = record(color, shape);
      cache.set(key, video);
    }
    const blob = await video.catch(() => undefined);
    if (!blob) return new HttpResponse(null, { status: 404 });
    return new HttpResponse(blob, { headers: { "Content-Type": "video/webm" } });
  }),
];
