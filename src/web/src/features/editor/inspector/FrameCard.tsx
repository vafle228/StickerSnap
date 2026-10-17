import { useState, type ChangeEvent, type KeyboardEvent } from "react";
import { Button, Card, Segmented, TextField } from "@stickersnap/ds";
import { useAppInfo } from "../../../shared/api/queries";
import type { Segment } from "../../../shared/api/types";
import { t } from "../../../shared/strings/ru";
import {
  defaultFrame,
  outputSize,
  sourceCrop,
  sourceStretch,
  squareCrop,
  squashStretch,
} from "../model/frame";
import { stretchLike, stretchOf } from "../model/crop";
import { glideNext } from "../glide";
import { editSegment, useEditor, useSegment } from "../model/store";
import styles from "./Inspector.module.css";

type Mode = Segment["frame"]["mode"];

/** «Кадр» (R3) of the selected sticker: crop or stretch, the presets and the output size. A
 *  preset is a proportion the crop frame holds while its corners are dragged; a free resize
 *  (Shift) leaves both unchecked. «Ко всем стикерам» copies the frame to the other segments. */
export function FrameCard() {
  const segment = useSegment();
  const source = useEditor((s) => s.source);
  const draft = useEditor((s) => s.frameDraft);
  const segments = useEditor((s) => s.scene?.segments);
  const limits = useAppInfo().data?.limits;
  if (!segment || !source) return null;

  const { frame } = segment;
  const side = limits?.[segment.preset].side;
  // While the frame is dragged on the canvas, the sizes follow it.
  const output = limits && outputSize(draft ?? frame, segment.preset, limits[segment.preset]);
  // A click moves the frame on the canvas in one glide, a drag there never does.
  const setFrame = (next: Segment["frame"]) => {
    glideNext();
    editSegment((s) => void (s.frame = next));
  };
  // The frame belongs to one sticker; another one is set the same way only when asked to.
  const differs = segments?.some((seg) => JSON.stringify(seg.frame) !== JSON.stringify(frame));
  const toAll = () => {
    glideNext();
    useEditor.getState().edit((scene) => {
      for (const seg of scene.segments) seg.frame = structuredClone(frame);
    });
  };

  return (
    <Card eyebrow={t("inspector.frameOf", { name: segment.name })} className={styles.card}>
      <Segmented<Mode>
        label={t("inspector.frameMode")}
        className={styles.wide}
        items={[
          { value: "crop", label: t("inspector.mode.crop") },
          { value: "stretch", label: t("inspector.mode.stretch") },
        ]}
        value={frame.mode}
        onChange={(mode) => mode !== frame.mode && setFrame(defaultFrame(mode, source))}
      />

      {frame.mode === "crop" ? (
        <>
          <div role="radiogroup" aria-label={t("inspector.cropPreset")} className={styles.pair}>
            <PresetButton
              checked={frame.preset === "square"}
              onClick={() => setFrame(squareCrop(source))}
            >
              {t("inspector.preset.square")}
            </PresetButton>
            <PresetButton
              checked={frame.preset === "source"}
              onClick={() => setFrame(sourceCrop(source))}
            >
              {t("inspector.preset.source")}
            </PresetButton>
          </div>
          <div className={styles.line}>
            <span>{t("inspector.output")}</span>
            <b>{output ? `${output.width} × ${output.height}` : "…"}</b>
          </div>
        </>
      ) : (
        <>
          {output && side !== undefined && (
            <StretchSize
              key={`${output.width}x${output.height}`}
              width={output.width}
              height={output.height}
              side={side}
              disabled={segment.preset === "emoji"}
              onChange={(size) =>
                setFrame(stretchOf(stretchLike(size, frame.aspect, source), source))
              }
            />
          )}
          <div role="radiogroup" aria-label={t("inspector.stretchPreset")} className={styles.pair}>
            <PresetButton
              checked={frame.preset === "source"}
              onClick={() => setFrame(sourceStretch(source))}
            >
              {t("inspector.preset.source")}
            </PresetButton>
            <PresetButton
              checked={frame.preset === "squash"}
              onClick={() => setFrame(squashStretch(source))}
            >
              {t("inspector.preset.squash")}
            </PresetButton>
          </div>
          {side !== undefined && (
            <p className={styles.hint}>{t("inspector.stretchHint", { side })}</p>
          )}
        </>
      )}

      {differs && (
        <Button size="sm" variant="ghost" icon="layers" className={styles.wide} onClick={toAll}>
          {t("inspector.frameToAll")}
        </Button>
      )}
    </Card>
  );
}

function PresetButton({
  checked,
  onClick,
  children,
}: {
  checked: boolean;
  onClick: () => void;
  children: string;
}) {
  return (
    <Button
      size="sm"
      variant="ghost"
      role="radio"
      aria-checked={checked}
      className={styles.preset}
      onClick={onClick}
    >
      {children}
    </Button>
  );
}

interface StretchSizeProps {
  width: number;
  height: number;
  side: number;
  disabled: boolean;
  onChange: (size: { width: number; height: number }) => void;
}

/** Width and height of the output; one side is always the preset's (512). A typed side stays as
 *  typed and the other becomes 512, so 512 × 288 with width 300 gives 300 × 512; a typed 512
 *  keeps the other side. No lock sits between the fields: with one side pinned at 512, a kept
 *  proportion leaves nothing to type; a typo goes back with undo. */
function StretchSize({ width, height, side, disabled, onChange }: StretchSizeProps) {
  const [draft, setDraft] = useState({ width: String(width), height: String(height) });
  const current = { width, height };

  const commit = (key: "width" | "height") => {
    const typed = clampSide(draft[key], side);
    if (typed === undefined || typed === current[key]) {
      setDraft({ width: String(width), height: String(height) });
      return;
    }
    const other = key === "width" ? "height" : "width";
    const next = { ...current, [key]: typed, [other]: typed < side ? side : current[other] };
    onChange(next);
  };
  const field = (key: "width" | "height") => ({
    className: styles.size,
    inputMode: "numeric" as const,
    value: draft[key],
    readOnly: disabled,
    onChange: (e: ChangeEvent<HTMLInputElement>) => setDraft({ ...draft, [key]: e.target.value }),
    onBlur: () => commit(key),
    onKeyDown: (e: KeyboardEvent<HTMLInputElement>) => {
      if (e.key === "Enter") commit(key);
    },
  });

  return (
    <div className={styles.sizes}>
      <TextField label={t("inspector.width")} {...field("width")} />
      <span className={styles.times} aria-hidden>
        ×
      </span>
      <TextField label={t("inspector.height")} {...field("height")} />
    </div>
  );
}

function clampSide(text: string, side: number): number | undefined {
  const value = Math.round(Number(text.trim()));
  if (!Number.isFinite(value) || value < 1) return undefined;
  return Math.min(side, value);
}
