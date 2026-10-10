import { useState, type MouseEvent } from "react";
import { Button, IconButton } from "../../../ds";
import { MenuPopover } from "../../../shared/overlays";
import { t } from "../../../shared/strings/ru";
import { num } from "../format";
import { segmentDuration } from "../model/frame";
import { useEditor } from "../model/store";
import styles from "./TimelineSlot.module.css";
import { addSegment, deleteSegment, type TimelineInfo } from "./useTimeline";

/** «Стикер 1 · 2,84 с», one per segment, the «⋯» menu of the chosen one and «+ Отрезок»
 *  (R18, artboard 3a). A right click on a tab opens the same menu there. */
export function SegmentTabs({ info }: { info: TimelineInfo }) {
  const { segments, segment, fps } = info;
  const [menu, setMenu] = useState<HTMLElement | null>(null);

  const choose = (id: string) => {
    const { select, setTime, setPlaying } = useEditor.getState();
    const next = segments.find((s) => s.id === id);
    if (!next || id === segment.id) return;
    setPlaying(false);
    select(id);
    setTime(next.inFrame / fps);
  };

  const openMenu = (id: string) => (e: MouseEvent<HTMLButtonElement>) => {
    e.preventDefault();
    choose(id);
    setMenu(e.currentTarget);
  };

  return (
    <div className={styles.tabs} role="group" aria-label={t("editor.segments")}>
      {segments.map((s) => (
        <Button
          key={s.id}
          size="sm"
          aria-pressed={s.id === segment.id}
          className={styles.tab}
          onClick={() => choose(s.id)}
          onContextMenu={openMenu(s.id)}
        >
          {t("editor.segment.tab", { name: s.name, duration: num(segmentDuration(s, fps), 2) })}
        </Button>
      ))}
      <IconButton
        icon="more"
        size="sm"
        flat
        label={t("editor.segment.menu", { name: segment.name })}
        aria-haspopup="menu"
        aria-expanded={menu !== null}
        onClick={(e) => setMenu(menu ? null : e.currentTarget)}
      />
      <Button
        size="sm"
        variant="ghost"
        icon="plus"
        title={t("editor.segment.addHint")}
        onClick={() => addSegment(info)}
      >
        {t("editor.segment.add")}
      </Button>
      <MenuPopover
        anchor={menu}
        open={menu !== null}
        placement="bottom-start"
        label={t("editor.segment.menu", { name: segment.name })}
        items={[
          {
            value: "delete",
            label: t("editor.segment.delete"),
            icon: "trash",
            disabled: segments.length < 2,
          },
        ]}
        onClose={() => setMenu(null)}
        onSelect={() => deleteSegment(info, segment.id)}
      />
    </div>
  );
}
