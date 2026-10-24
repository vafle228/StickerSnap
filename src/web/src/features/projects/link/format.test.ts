import { describe, expect, it } from "vitest";
import type { Download, LinkVideo } from "../../../shared/api/types";
import {
  amount,
  downloadMeta,
  duration,
  eta,
  isLong,
  longNote,
  megabytes,
  percentOf,
} from "./format";

const MB = 1024 * 1024;

const video: LinkVideo = {
  site: "youtube",
  url: "https://youtu.be/a",
  title: "Кот смотрит в окно",
  durationSec: 48,
  bytes: 38 * MB,
  quality: "720p",
};

const download = (patch: Partial<Download> = {}): Download => ({
  id: "d1",
  video,
  state: "downloading",
  receivedBytes: 13 * MB,
  createdAt: "2026-10-24T06:00:00Z",
  ...patch,
});

describe("the words of a download (artboard 20, 3·5)", () => {
  it("writes lengths as a clock", () => {
    expect(duration(48)).toBe("0:48");
    expect(duration(4360)).toBe("1:12:40");
    expect(duration(2.4)).toBe("0:02");
  });

  it("counts megabytes, with a decimal below one", () => {
    expect(megabytes(38 * MB)).toBe("38");
    expect(megabytes(0.9 * MB)).toBe("0,9");
  });

  it("says how long is left in seconds, minutes or hours", () => {
    expect(eta(20)).toBe("ещё ~20 с");
    expect(eta(185)).toBe("ещё ~3 мин");
    expect(eta(4200)).toBe("ещё ~1 ч 10 мин");
  });

  it("gives the card's line, megabytes and percent", () => {
    expect(downloadMeta(download())).toBe("YouTube · 0:48 · скачиваю 720p");
    expect(downloadMeta(download({ state: "queued" }))).toBe(
      "YouTube · 0:48 · ждёт, пока скачается предыдущее",
    );
    expect(amount(download())).toBe("13 из 38 МБ");
    expect(percentOf(download())).toBe(34);
  });

  it("warns of a video over 10 minutes, with its length and weight", () => {
    const long = { ...video, durationSec: 4360, bytes: 410 * MB };
    expect(isLong(video)).toBe(false);
    expect(isLong({ ...video, durationSec: 600 })).toBe(false);
    expect(isLong(long)).toBe(true);
    expect(longNote(long)).toBe("Ролик длинный: 1:12:40, около 410 МБ. Ждать придётся долго");
  });
});
