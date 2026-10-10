import { useState } from "react";
import { TextField } from "../../ds";
import { t } from "../../shared/strings/ru";
import { EMOJI_GROUPS, searchEmoji } from "./emoji";
import styles from "./PublishDialog.module.css";

export const MAX_EMOJI = 20;

interface EmojiPickerProps {
  name: string;
  value: readonly string[];
  recent: readonly string[];
  onPick: (emoji: string) => void;
}

/** «Выбор эмодзи» beside the publish window (artboard 7): a click adds an emoji to the sticker,
 *  a second click takes it off. The first one is the main one. */
export function EmojiPicker({ name, value, recent, onPick }: EmojiPickerProps) {
  const [query, setQuery] = useState("");
  const found = searchEmoji(query);
  const full = value.length >= MAX_EMOJI;

  const grid = (list: readonly string[]) => (
    <div className={styles.emojiGrid}>
      {list.map((emoji) => {
        const on = value.includes(emoji);
        return (
          <button
            key={emoji}
            type="button"
            className={styles.emojiCell}
            aria-pressed={on}
            disabled={full && !on}
            onClick={() => onPick(emoji)}
          >
            {emoji}
          </button>
        );
      })}
    </div>
  );

  return (
    <div className={styles.picker} role="dialog" aria-label={t("publish.emoji.title", { name })}>
      <div className={styles.pickerHead}>
        <span className={styles.pickerTitle}>{t("publish.emoji.title", { name })}</span>
        <span className={styles.pickerCount}>
          {t("publish.emoji.count", { n: value.length, max: MAX_EMOJI })}
        </span>
      </div>
      <TextField
        icon="search"
        placeholder={t("publish.emoji.search")}
        aria-label={t("publish.emoji.searchLabel")}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        autoFocus
      />
      {query.trim() ? (
        <section className={styles.emojiGroup}>
          <h3 className={styles.emojiGroupTitle}>{t("publish.emoji.found")}</h3>
          {found.length > 0 ? (
            grid(found)
          ) : (
            <p className={styles.pickerNote}>{t("publish.emoji.nothing")}</p>
          )}
        </section>
      ) : (
        <div className={styles.emojiScroll}>
          {recent.length > 0 && (
            <section className={styles.emojiGroup}>
              <h3 className={styles.emojiGroupTitle}>{t("publish.emoji.recent")}</h3>
              {grid(recent)}
            </section>
          )}
          {EMOJI_GROUPS.map((g) => (
            <section key={g.id} className={styles.emojiGroup}>
              <h3 className={styles.emojiGroupTitle}>{t(`publish.emoji.${g.id}`)}</h3>
              {grid(g.list.map(([emoji]) => emoji))}
            </section>
          ))}
        </div>
      )}
      <p className={styles.pickerNote}>{t("publish.emoji.note")}</p>
    </div>
  );
}
