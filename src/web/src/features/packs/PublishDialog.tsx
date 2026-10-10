import { useQuery } from "@tanstack/react-query";
import { useState, type ReactNode } from "react";
import {
  Badge,
  Banner,
  Button,
  Checkbox,
  Dialog,
  Icon,
  Segmented,
  Select,
  TextField,
} from "../../ds";
import { useApp } from "../../app/store";
import { api, call } from "../../shared/api/client";
import { useAccount, useAppInfo, useProjects, usePublishDraft } from "../../shared/api/queries";
import type { AccountLink, PublishDraft } from "../../shared/api/types";
import { Popover } from "../../shared/overlays";
import { t } from "../../shared/strings/ru";
import { kilobytes, seconds } from "../editor/format";
import { useDebounced } from "../editor/useDebounced";
import { EmojiPicker, MAX_EMOJI } from "./EmojiPicker";
import { packMeta, placeholder, sendLabel } from "./format";
import styles from "./PublishDialog.module.css";
import { usePacksUi, type PublishStart } from "./store";

type Segment = PublishDraft["segments"][number];
type DraftPack = PublishDraft["packs"][number];
type Where = "existing" | "new";

const TITLE_MAX = 64;
const LINK_MAX = 43;

/** The publish window (artboard 7, «одно окно, три вопроса»): which stickers, with which emoji,
 *  into which pack. Every way in opens this one window with different things already picked.
 *  Sending itself is stage 1c: the rainbow button waits, disabled. */
export function PublishDialog() {
  const start = usePacksUi((s) => s.publish);
  const close = usePacksUi((s) => s.closePublish);
  const account = useAccount().data;
  const projects = useProjects().data ?? [];
  const [chosen, setChosen] = useState<string>();
  const projectId = start?.projectId ?? chosen ?? projects[0]?.id;
  const draft = usePublishDraft(start ? projectId : undefined);

  if (!start) return null;
  const done = () => {
    setChosen(undefined);
    close();
  };
  const user = account?.state === "linked" || account?.state === "error" ? account.user : undefined;

  return (
    <Dialog
      wide
      className={styles.dialog}
      title={t("publish.title")}
      text={
        user
          ? t("publish.account", { name: user.username ? `@${user.username}` : user.firstName })
          : undefined
      }
      onClose={done}
    >
      {isLinked(account) ? (
        draft.data ? (
          <PublishForm
            // Another project starts the questions over
            key={projectId}
            start={start}
            draft={draft.data}
            project={
              <Select
                className={styles.project}
                label={t("publish.project")}
                placeholder={t("publish.pickProject")}
                items={projects.map((p) => ({ value: p.id, label: p.name }))}
                value={projectId}
                onChange={setChosen}
              />
            }
            projectLocked={start.projectId !== undefined}
            projectName={projects.find((p) => p.id === projectId)?.name}
            onCancel={done}
          />
        ) : (
          <div className={styles.loading} aria-busy />
        )
      ) : (
        <Unlinked onCancel={done} />
      )}
    </Dialog>
  );
}

/** Unknown yet counts as linked: the questions appear, the send waits for the server anyway. */
function isLinked(account: AccountLink | undefined) {
  return account === undefined || account.state === "linked" || account.state === "error";
}

/** 7-2: without a linked account the window asks for one instead of its questions. */
function Unlinked({ onCancel }: { onCancel: () => void }) {
  const bot = useAppInfo().data?.botUsername ?? "StickerSnapAppBot";
  const setSection = useApp((s) => s.setSection);
  return (
    <div className={styles.body}>
      <Banner
        tone="warning"
        title={t("packs.unlinked.title")}
        description={t("packs.unlinked.text", { bot })}
      />
      <div className={styles.foot}>
        <span className={styles.status} />
        <Button variant="ghost" onClick={onCancel}>
          {t("common.cancel")}
        </Button>
        <Button
          variant="primary"
          onClick={() => {
            onCancel();
            setSection("settings");
          }}
        >
          {t("packs.unlinked.action")}
        </Button>
      </div>
    </div>
  );
}

interface PublishFormProps {
  start: PublishStart;
  draft: PublishDraft;
  project: ReactNode;
  projectLocked: boolean;
  projectName?: string;
  onCancel: () => void;
}

function PublishForm({
  start,
  draft,
  project,
  projectLocked,
  projectName,
  onCancel,
}: PublishFormProps) {
  const replace = start.replace;
  const shown = replace
    ? draft.segments.filter((s) => s.segmentId === replace.segmentId)
    : draft.segments;
  const emojiPreset = shown.length > 0 && shown.every((s) => s.preset === "emoji");
  const packs = draft.packs.filter((p) => p.reason !== "wrong_type");
  const wrongType = draft.packs.filter((p) => p.reason === "wrong_type");
  const firstPack = [start.pack, draft.lastPack].find((name) =>
    packs.some((p) => p.name === name && p.selectable),
  );

  const [checked, setChecked] = useState<Set<string>>(
    () => new Set(shown.filter((s) => s.export?.verdict !== "fail").map((s) => s.segmentId)),
  );
  const [emoji, setEmoji] = useState<Record<string, string[]>>(() =>
    replace ? { [replace.segmentId]: replace.emoji } : {},
  );
  const [where, setWhere] = useState<Where>(
    start.newPack || (!firstPack && !replace) ? "new" : "existing",
  );
  const [pack, setPack] = useState(firstPack);
  const [picker, setPicker] = useState<{ id: string; anchor: HTMLElement } | null>(null);
  const [title, setTitle] = useState("");
  const [link, setLink] = useState<string>();

  const toggle = (id: string, on: boolean) =>
    setChecked((set) => {
      const next = new Set(set);
      if (on) next.add(id);
      else next.delete(id);
      return next;
    });
  // A click on an emoji adds it, a click on a chosen one takes it off
  const pick = (id: string, e: string) =>
    setEmoji((all) => {
      const list = all[id] ?? [];
      const next = list.includes(e)
        ? list.filter((x) => x !== e)
        : [...list, e].slice(0, MAX_EMOJI);
      return { ...all, [id]: next };
    });

  const name = useNameCheck(where === "new" ? title : "", link);
  const selected = shown.filter((s) => checked.has(s.segmentId));
  const needEmoji = selected.find((s) => (emoji[s.segmentId] ?? []).length === 0);
  const targetName = where === "existing" ? pack : undefined;
  const target = packs.find((p) => p.name === targetName);
  const pickerSegment = shown.find((s) => s.segmentId === picker?.id);

  // The footer says what still stops the send, the first thing first
  const status = (() => {
    if (shown.length === 0) return undefined;
    if (selected.length === 0) return { text: t("publish.needSticker"), warn: true };
    if (needEmoji) return { text: t("publish.needEmoji", { name: needEmoji.name }), warn: true };
    if (where === "existing" && !target) return { text: t("publish.needPack"), warn: true };
    return { text: t("publish.ready"), warn: false };
  })();

  return (
    <div className={styles.body}>
      <section className={styles.question}>
        <div className={styles.qHead}>
          <span className={styles.qNum} aria-hidden>
            1
          </span>
          <h3 className={styles.qTitle}>{t("publish.q1")}</h3>
          {projectLocked ? (
            <span className={styles.projectFixed}>
              {t("publish.project")} <b>{projectName}</b>
            </span>
          ) : (
            project
          )}
        </div>
        {shown.length === 0 && <p className={styles.note}>{t("publish.noSegments")}</p>}
        <ul className={styles.rows}>
          {shown.map((s, i) => (
            <SegmentRow
              key={s.segmentId}
              segment={s}
              index={i}
              checked={checked.has(s.segmentId)}
              emoji={emoji[s.segmentId] ?? []}
              replaces={replaceNote(s, targetName, draft.packs)}
              pickerOpen={picker?.id === s.segmentId}
              onCheck={(on) => toggle(s.segmentId, on)}
              onRemoveEmoji={(e) => pick(s.segmentId, e)}
              onPicker={(anchor) =>
                setPicker((p) => (p?.id === s.segmentId ? null : { id: s.segmentId, anchor }))
              }
            />
          ))}
        </ul>
      </section>

      <section className={styles.question}>
        <div className={styles.qHead}>
          <span className={styles.qNum} aria-hidden>
            2
          </span>
          <h3 className={styles.qTitle}>{t("publish.q2")}</h3>
          {!replace && (
            <Segmented<Where>
              label={t("publish.where")}
              items={[
                { value: "existing", label: t("publish.existing") },
                { value: "new", label: t("publish.new") },
              ]}
              value={where}
              onChange={setWhere}
            />
          )}
        </div>
        {where === "existing" ? (
          <>
            {packs.length === 0 ? (
              <p className={styles.note}>{t("publish.noPacks")}</p>
            ) : (
              <div className={styles.targets} role="radiogroup" aria-label={t("publish.q2")}>
                {packs.map((p) => (
                  <PackTarget
                    key={p.name}
                    pack={p}
                    recent={p.name === draft.lastPack}
                    checked={p.name === pack}
                    locked={replace !== undefined}
                    onPick={() => setPack(p.name)}
                  />
                ))}
              </div>
            )}
            {wrongType.map((p) => (
              <p key={p.name} className={styles.note}>
                {t(emojiPreset ? "publish.wrongTypeEmoji" : "publish.wrongType", {
                  title: p.title,
                })}
              </p>
            ))}
          </>
        ) : (
          <NewPack
            title={title}
            onTitle={setTitle}
            link={link ?? name?.name ?? ""}
            onLink={setLink}
            check={name}
            emoji={emojiPreset}
          />
        )}
      </section>

      <div className={styles.foot}>
        <span className={styles.status} data-warn={status?.warn}>
          {status?.text}
        </span>
        <Button variant="ghost" onClick={onCancel}>
          {t("common.cancel")}
        </Button>
        {/* Sending to Telegram is stage 1c: the window is all there, the send waits */}
        <Button variant="prism" icon="send" disabled>
          {sendLabel(selected.length)}
        </Button>
      </div>

      <Popover
        anchor={picker?.anchor ?? null}
        open={pickerSegment !== undefined}
        onClose={() => setPicker(null)}
        placement="right-start"
      >
        {pickerSegment && (
          <EmojiPicker
            name={pickerSegment.name}
            value={emoji[pickerSegment.segmentId] ?? []}
            recent={draft.recentEmoji}
            onPick={(e) => pick(pickerSegment.segmentId, e)}
          />
        )}
      </Popover>
    </div>
  );
}

/** The pack whose old version a segment replaces, when it was already sent there. */
function replaceNote(s: Segment, target: string | undefined, packs: readonly DraftPack[]) {
  const ref = s.publishedIn.find((r) => r.pack === target);
  return ref ? packs.find((p) => p.name === ref.pack)?.title : undefined;
}

interface SegmentRowProps {
  segment: Segment;
  index: number;
  checked: boolean;
  emoji: string[];
  replaces?: string;
  pickerOpen: boolean;
  onCheck: (on: boolean) => void;
  onRemoveEmoji: (emoji: string) => void;
  onPicker: (anchor: HTMLElement) => void;
}

/** One segment: send it or not, its export and check, its emoji. */
function SegmentRow({
  segment: s,
  index,
  checked,
  emoji,
  replaces,
  pickerOpen,
  onCheck,
  onRemoveEmoji,
  onPicker,
}: SegmentRowProps) {
  const limits = useAppInfo().data?.limits;
  const limit = s.preset === "emoji" ? limits?.emoji.maxBytes : limits?.sticker.maxBytes;
  const failed = s.export?.verdict === "fail";
  const needs = checked && emoji.length === 0;
  const art = placeholder(index + 1);

  const note = (() => {
    if (failed && s.export) {
      return t("publish.failedText", {
        size: kilobytes(s.export.bytes),
        limit: kilobytes(limit ?? 262_144),
      });
    }
    if (replaces) return t("publish.replaces", { pack: replaces });
    if (!s.export) return t("publish.notExported");
    if (s.export.stale) return t("publish.stale");
    return t("publish.passed", { size: kilobytes(s.export.bytes) });
  })();
  const tone = failed
    ? "danger"
    : replaces
      ? "primary"
      : s.export && !s.export.stale
        ? "success"
        : "muted";

  return (
    <li className={styles.row} data-need={needs} data-off={!checked}>
      <Checkbox
        checked={checked}
        disabled={failed}
        onChange={onCheck}
        label={<span className={styles.srOnly}>{t("publish.row", { name: s.name })}</span>}
      />
      <span className={styles.rowArt} aria-hidden>
        <span style={{ background: art.tint, borderRadius: art.radius }} />
      </span>
      <span className={styles.rowText}>
        <span className={styles.rowName}>
          {t("publish.rowName", { name: s.name, duration: seconds(s.durationSec) })}
          {failed && (
            <Badge tone="danger" icon="error-circle">
              {t("publish.failed")}
            </Badge>
          )}
        </span>
        <span className={styles.rowNote} data-tone={tone}>
          {note}
        </span>
      </span>
      {failed ? (
        // The check itself is in the editor's «Экспорт», next to the file
        <Button size="sm" onClick={() => useApp.getState().setSection("editor")}>
          {t("publish.openCheck")}
        </Button>
      ) : (
        <span className={styles.rowEmoji}>
          {emoji.map((e) => (
            <span key={e} className={styles.emojiChip}>
              {e}
              <button
                type="button"
                aria-label={t("publish.removeEmoji", { emoji: e })}
                onClick={() => onRemoveEmoji(e)}
              >
                <Icon name="close" size={14} strokeWidth={2} />
              </button>
            </span>
          ))}
          {emoji.length < MAX_EMOJI && (
            <button
              type="button"
              className={styles.addEmoji}
              data-need={needs}
              data-open={pickerOpen}
              aria-expanded={pickerOpen}
              aria-haspopup="dialog"
              onClick={(e) => onPicker(e.currentTarget)}
            >
              <Icon name="smile" size={16} />
              {emoji.length ? t("publish.moreEmoji") : t("publish.pickEmoji")}
            </button>
          )}
        </span>
      )}
    </li>
  );
}

interface PackTargetProps {
  pack: DraftPack;
  recent: boolean;
  checked: boolean;
  locked: boolean;
  onPick: () => void;
}

/** A pack to send into; a full or vanished one stays in view but cannot be picked (7-5). */
function PackTarget({ pack, recent, checked, locked, onPick }: PackTargetProps) {
  const meta = packMeta(pack);
  const off = !pack.selectable || (locked && !checked);
  return (
    <label className={styles.target} data-checked={checked} data-off={off}>
      <input type="radio" name="publish-pack" checked={checked} disabled={off} onChange={onPick} />
      <span className={styles.targetText}>
        <span className={styles.targetTitle}>{pack.title}</span>
        <span className={styles.targetMeta} data-tone={meta.tone}>
          {recent && meta.tone === "normal" ? t("publish.recent", { fill: meta.text }) : meta.text}
        </span>
      </span>
    </label>
  );
}

/** The server's word on a new pack's link: transliterated from the title, checked in Telegram. */
function useNameCheck(title: string, name: string | undefined) {
  const request = useDebounced({ title: title.trim(), name: name?.trim() || undefined }, 300);
  const query = useQuery({
    queryKey: ["pack-name", request.title, request.name],
    queryFn: () => call(api.POST("/api/v1/packs/name-check", { body: request })),
    enabled: Boolean(request.title || request.name),
    staleTime: 30_000,
    placeholderData: (previous) => previous,
  });
  return query.data;
}

interface NewPackProps {
  title: string;
  onTitle: (title: string) => void;
  link: string;
  onLink: (link: string) => void;
  check: ReturnType<typeof useNameCheck>;
  emoji: boolean;
}

/** «Новый пак» in the same window: a title, the link made from it, the type from the preset. */
function NewPack({ title, onTitle, link, onLink, check, emoji }: NewPackProps) {
  const bot = useAppInfo().data?.botUsername ?? "StickerSnapAppBot";
  const problem = check?.name === link ? check.problems[0] : undefined;
  const taken = problem === "taken";
  const suggestion = check?.suggestion;
  const error = problem
    ? taken
      ? `${t("publish.name.taken", { name: link })}. ${t("publish.name.takenText")}`
      : t(`publish.name.${problem}`)
    : undefined;

  return (
    <div className={styles.newPack}>
      <div className={styles.newField}>
        <span className={styles.newLabel}>
          <span>{t("publish.packTitle")}</span>
          <span className={styles.counter}>
            {title.length} / {TITLE_MAX}
          </span>
        </span>
        <TextField
          aria-label={t("publish.packTitle")}
          value={title}
          maxLength={TITLE_MAX}
          placeholder="Кот и все-все-все"
          onChange={(e) => onTitle(e.target.value)}
        />
      </div>
      <div className={styles.newField}>
        <span className={styles.newLabel}>
          <span>{t("publish.packLink")}</span>
          <span className={styles.counter}>
            {link.length} / {LINK_MAX}
          </span>
        </span>
        <TextField
          className={styles.link}
          aria-label={t("publish.packLinkOwn")}
          icon="link"
          value={link}
          maxLength={LINK_MAX}
          suffix={`_by_${bot}`}
          hint={t("publish.packLinkHint")}
          error={error}
          fix={
            taken && suggestion
              ? {
                  label: t("publish.name.take", { name: suggestion }),
                  onClick: () => onLink(suggestion),
                }
              : undefined
          }
          onChange={(e) => onLink(e.target.value)}
        />
      </div>
      <div className={styles.newType}>
        {t("publish.packType")}
        <Badge>{t(emoji ? "publish.packTypeEmoji" : "publish.packTypeSticker")}</Badge>
      </div>
      <p className={styles.note}>{t("publish.newNote")}</p>
    </div>
  );
}
