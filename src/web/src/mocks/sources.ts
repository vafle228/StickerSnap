import type { PresetLimits, SourceInfo, ValidationReport } from "../shared/api/types";

/* What the demo server reads from a file: its name and bytes stand in for ffprobe. */

const FORMATS = ["mp4", "mov", "webm", "gif", "webp"] as const;
type Format = (typeof FORMATS)[number];

export function extOf(name: string): string {
  return name.includes(".") ? (name.split(".").pop() ?? "").toLowerCase() : "";
}

export function isFormat(ext: string): ext is Format {
  return (FORMATS as readonly string[]).includes(ext);
}

/** The demo video's properties under the file's own name: a GIF reads as a short 12 fps clip
 *  with transparency, WEBM and WEBP keep their alpha, the rest are 8 s of 30 fps. */
export function sourceOf(name: string, sizeBytes: number, template: SourceInfo): SourceInfo {
  const format = extOf(name) as Format;
  const base: SourceInfo = { ...template, fileName: name, sizeBytes, status: "ok", format };
  delete base.path;
  switch (format) {
    case "gif":
      return {
        ...base,
        codec: "gif",
        width: 480,
        height: 480,
        fps: { num: 12, den: 1 },
        frameCount: 18,
        durationSec: 1.5,
        hasAlpha: true,
      };
    case "webm":
      return { ...base, codec: "vp9", hasAlpha: true };
    case "webp":
      return { ...base, codec: "webp", hasAlpha: true };
    case "mov":
      return { ...base, codec: "prores" };
    default:
      return { ...base, codec: "h264" };
  }
}

/** A project's name from its video: «cat-keyboard.gif» → «cat-keyboard». */
export function projectName(fileName: string): string {
  return fileName.replace(/\.[^.]+$/, "") || fileName;
}

export interface Facts {
  fileName: string;
  bytes: number;
  container: string;
  codec: string;
  width: number;
  height: number;
  durationSec: number;
  fps: number;
  audio: string;
  alpha: string;
}

/** The rules of Telegram's table (plan, «Ограничения Telegram») applied to a file. Longer than
 *  3 s is a warning, not a failure: the long sticker of R19 is «экспериментальный». */
export function validationOf(facts: Facts, limits: PresetLimits): ValidationReport {
  const side = Math.max(facts.width, facts.height);
  const checks: ValidationReport["checks"] = [
    {
      rule: "container",
      status: facts.container === "webm" ? "pass" : "fail",
      expected: "webm",
      actual: facts.container,
    },
    {
      rule: "codec",
      status: facts.codec === "vp9" ? "pass" : "fail",
      expected: "vp9",
      actual: facts.codec,
    },
    {
      rule: "size",
      status: facts.bytes <= limits.maxBytes ? "pass" : "fail",
      expected: `≤ ${limits.maxBytes}`,
      actual: String(facts.bytes),
    },
    {
      rule: "dimensions",
      status: side === limits.side ? "pass" : "fail",
      expected: String(limits.side),
      actual: `${facts.width}×${facts.height}`,
    },
    {
      rule: "duration",
      status: facts.durationSec <= limits.maxDurationSec + 1e-6 ? "pass" : "warn",
      expected: `≤ ${limits.maxDurationSec}`,
      actual: facts.durationSec.toFixed(2),
    },
    {
      rule: "fps",
      status: facts.fps <= limits.maxFps ? "pass" : "fail",
      expected: `≤ ${limits.maxFps}`,
      actual: String(Math.round(facts.fps)),
    },
    {
      rule: "audio",
      status: facts.audio === "none" ? "pass" : "fail",
      expected: "none",
      actual: facts.audio,
    },
    { rule: "alpha", status: "pass", expected: "any", actual: facts.alpha },
  ];
  const verdict = checks.some((c) => c.status === "fail")
    ? "fail"
    : checks.some((c) => c.status === "warn")
      ? "experimental"
      : "pass";
  return { fileName: facts.fileName, bytes: facts.bytes, verdict, checks };
}

type Probed = { width: number; height: number; durationSec: number };

/** Width, height and length of a video file, read by the browser itself; undefined when it
 *  cannot play it. A MediaRecorder file has no length in its header until seeked to the end. */
export function probe(file: Blob): Promise<Probed | undefined> {
  if (typeof document === "undefined") return Promise.resolve(undefined);
  return new Promise((resolve) => {
    const video = document.createElement("video");
    const url = URL.createObjectURL(file);
    let settled = false;
    const done = (value?: Probed) => {
      if (settled) return;
      settled = true;
      URL.revokeObjectURL(url);
      resolve(value);
    };
    const finish = () =>
      done({ width: video.videoWidth, height: video.videoHeight, durationSec: video.duration });
    video.muted = true;
    video.preload = "metadata";
    video.onerror = () => done();
    video.onloadedmetadata = () => {
      if (Number.isFinite(video.duration)) {
        finish();
        return;
      }
      video.ondurationchange = () => {
        if (Number.isFinite(video.duration)) finish();
      };
      video.currentTime = 1e9;
    };
    setTimeout(() => done(), 4000);
    video.src = url;
  });
}
