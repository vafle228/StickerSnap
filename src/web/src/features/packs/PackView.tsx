import { useState } from "react";
import {
  Badge,
  Banner,
  Button,
  Card,
  DropZone,
  IconButton,
  LinkChip,
  type MenuItem,
} from "../../ds";
import { useApp } from "../../app/store";
import { useAppInfo, usePack } from "../../shared/api/queries";
import type { PackSticker, PackSummary } from "../../shared/api/types";
import { MenuPopover } from "../../shared/overlays";
import { t } from "../../shared/strings/ru";
import { when } from "../projects/format";
import { usePackActions } from "./actions";
import { isFull, packBadge, placeholder, shortLink } from "./format";
import styles from "./PacksScreen.module.css";
import { usePacksUi } from "./store";

type Action = "open" | "reveal" | "replace" | "remove";

/** The open pack (artboard 7, right): its link, the last check with Telegram, and its stickers
 *  with their emoji and the project each came from. Everything else is in a sticker's «⋯». */
export function PackView({ summary }: { summary: PackSummary }) {
  const pack = usePack(summary.name).data;
  const desktop = useAppInfo().data?.mode === "desktop";
  const actions = usePackActions();
  const openProject = useApp((s) => s.openProject);
  const startPublish = usePacksUi((s) => s.startPublish);
  const [menu, setMenu] = useState<{ id: string; anchor: HTMLElement } | null>(null);

  const gone = summary.status === "missing";
  const full = isFull(summary);
  const link = pack?.link ?? `https://t.me/addstickers/${summary.name}`;
  const stickers = pack?.stickers ?? [];
  const menuSticker = stickers.find((s) => s.id === menu?.id);
  const origin = menuSticker?.origin;
  const hasProject = origin !== undefined && origin.projectStatus === "ok";

  const items: (MenuItem<Action> | "-")[] = [
    { value: "open", label: t("packs.openProject"), icon: "film", disabled: !hasProject },
    ...(desktop
      ? [
          {
            value: "reveal" as const,
            label: t("packs.revealWebm"),
            icon: "folder" as const,
            disabled: !hasProject,
          },
        ]
      : []),
    { value: "replace", label: t("packs.replace"), icon: "refresh", disabled: !hasProject || gone },
    "-",
    { value: "remove", label: t("packs.remove"), icon: "trash", danger: true, disabled: gone },
  ];

  const act = (action: Action, sticker: PackSticker) => {
    const from = sticker.origin;
    if (action === "remove") void actions.removeSticker(summary.name, sticker);
    if (!from) return;
    if (action === "open") openProject(from.projectId);
    if (action === "reveal") void actions.revealWebm(from);
    if (action === "replace") {
      startPublish({
        projectId: from.projectId,
        pack: summary.name,
        replace: { segmentId: from.segmentId, stickerId: sticker.id, emoji: sticker.emoji },
      });
    }
  };

  return (
    <Card className={styles.view} aria-label={summary.title}>
      {gone && (
        <Banner
          tone="warning"
          title={t("packs.gone.title", { title: summary.title })}
          description={t("packs.gone.text")}
          action={
            <Button size="sm" onClick={() => void actions.forget(summary)}>
              {t("packs.gone.action")}
            </Button>
          }
        />
      )}

      <div className={styles.viewHead}>
        <div className={styles.viewHeading}>
          <div className={styles.viewTitleRow}>
            <h2 className={styles.viewTitle}>{summary.title}</h2>
            <Badge tone={full ? "warning" : "primary"} icon={full ? "alert" : undefined}>
              {packBadge(summary)}
            </Badge>
          </div>
          <LinkChip href={link} label={shortLink(link)} className={styles.chip} />
          <p className={styles.checked}>
            {summary.checkedAt
              ? t("packs.checked", { when: when(summary.checkedAt) })
              : t("packs.notChecked")}
            {" · "}
            <button
              type="button"
              className={styles.link}
              onClick={() => void actions.sync(summary.name)}
            >
              {t("packs.sync")}
            </button>
          </p>
        </div>
        {!gone && (
          <Button
            variant="primary"
            icon="external"
            onClick={() => window.open(link, "_blank", "noopener,noreferrer")}
          >
            {t("packs.openTelegram")}
          </Button>
        )}
      </div>

      <ul className={styles.grid} aria-label={t("packs.stickers")}>
        {stickers.map((s, i) => (
          <li key={s.id}>
            <StickerCell
              sticker={s}
              index={i}
              menuOpen={menu?.id === s.id}
              onMenu={(anchor) => setMenu((m) => (m?.id === s.id ? null : { id: s.id, anchor }))}
              onOpenProject={openProject}
            />
          </li>
        ))}
        {!gone && !full && (
          <li>
            <DropZone
              variant="tile"
              className={styles.add}
              icon="plus"
              title={t("packs.add")}
              hint={t("packs.addHint")}
              onClick={() => startPublish({ pack: summary.name })}
            />
          </li>
        )}
      </ul>

      <MenuPopover<Action>
        anchor={menu?.anchor ?? null}
        open={menuSticker !== undefined}
        onClose={() => setMenu(null)}
        placement="bottom-start"
        label={t("packs.stickerMenu")}
        menuClassName={styles.menu}
        items={items}
        onSelect={(action) => menuSticker && act(action, menuSticker)}
      />
    </Card>
  );
}

interface StickerCellProps {
  sticker: PackSticker;
  index: number;
  menuOpen: boolean;
  onMenu: (anchor: HTMLElement) => void;
  onOpenProject: (id: string) => void;
}

/** One sticker: its art on the checker, its emoji, and where it came from. */
function StickerCell({ sticker, index, menuOpen, onMenu, onOpenProject }: StickerCellProps) {
  const art = placeholder(index);
  const origin = sticker.origin;
  return (
    <div className={styles.sticker} data-menu={menuOpen}>
      <div className={styles.stickerArt}>
        <span
          className={styles.stickerShape}
          style={{ background: art.tint, borderRadius: art.radius }}
          aria-hidden
        />
        <IconButton
          icon="more"
          label={t("packs.stickerMenu")}
          flat
          size="sm"
          className={styles.stickerMenu}
          aria-haspopup="menu"
          aria-expanded={menuOpen}
          onClick={(e) => onMenu(e.currentTarget)}
        />
      </div>
      <span
        className={styles.stickerEmoji}
        aria-label={t("packs.emoji", { emoji: sticker.emoji.join(" ") })}
      >
        {sticker.emoji.join(" ")}
      </span>
      {origin?.projectStatus === "ok" ? (
        <button
          type="button"
          className={styles.stickerSrc}
          title={t("packs.fromProject", { name: origin.projectName })}
          onClick={() => onOpenProject(origin.projectId)}
        >
          {origin.projectName}
        </button>
      ) : (
        <span className={styles.stickerSrc} data-tone={origin ? "warning" : "muted"}>
          {origin ? t("packs.projectMissing") : t("packs.noProject")}
        </span>
      )}
    </div>
  );
}
