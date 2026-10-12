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
  /** To the end of the last frame */
  durationSec: number;
  /** What the WEBM header says (Info › Duration), when it differs: a long sticker of R19 */
  declaredDurationSec?: number;
  fps: number;
  audio: string;
  alpha: string;
}

/** A frame at 30 fps: a container's duration rounds up to it. */
const DURATION_SLACK = 1 / 30;

/** The rules of Telegram's table (plan, «Ограничения Telegram») applied to a file, as the server's
 *  validator does (contract, G4). A header that declares at most 3 s over frames that run longer
 *  is a warning: the long sticker of R19 is «экспериментальный»; an honest 4 s file fails. An emoji
 *  is a square of the preset's side, a sticker has one side of it. */
export function validationOf(facts: Facts, limits: PresetLimits, emoji = false): ValidationReport {
  const side = Math.max(facts.width, facts.height);
  const maxSec = limits.maxDurationSec + DURATION_SLACK;
  const declared = facts.declaredDurationSec ?? facts.durationSec;
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
    emoji
      ? {
          rule: "dimensions",
          status: facts.width === limits.side && facts.height === limits.side ? "pass" : "fail",
          expected: `${limits.side}×${limits.side}`,
          actual: `${facts.width}×${facts.height}`,
        }
      : {
          rule: "dimensions",
          status: side === limits.side ? "pass" : "fail",
          expected: String(limits.side),
          actual: `${facts.width}×${facts.height}`,
        },
    {
      rule: "duration",
      status: declared > maxSec ? "fail" : facts.durationSec > maxSec ? "warn" : "pass",
      expected: `≤ ${limits.maxDurationSec}`,
      actual: String(Math.round(facts.durationSec * 100) / 100),
    },
    {
      rule: "fps",
      status: facts.fps <= limits.maxFps + 0.01 ? "pass" : "fail",
      expected: `≤ ${limits.maxFps}`,
      actual: String(Math.round(facts.fps * 100) / 100),
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
