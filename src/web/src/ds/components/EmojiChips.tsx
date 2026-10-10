import { cx } from "../cx";
import { Icon } from "./Icon";
import { IconButton } from "./IconButton";

export interface EmojiChipsProps {
  /** 1–20 emoji; the last one cannot be removed */
  value: readonly string[];
  max?: number;
  onRemove?: (index: number) => void;
  /** Opens the emoji picker popover */
  onAdd?: () => void;
  label?: string;
  className?: string;
}

/** Telegram emoji of a sticker. */
export function EmojiChips({
  value,
  max = 20,
  onRemove,
  onAdd,
  label = "Эмодзи стикера",
  className,
}: EmojiChipsProps) {
  return (
    <div className={cx("ss-emojis", className)} role="group" aria-label={label}>
      {value.map((emoji, i) => (
        <span key={`${emoji}-${i}`} className="ss-emoji">
          {emoji}
          {value.length > 1 && onRemove && (
            <button type="button" aria-label={`Убрать ${emoji}`} onClick={() => onRemove(i)}>
              <Icon name="close" size={14} strokeWidth={2} />
            </button>
          )}
        </span>
      ))}
      {value.length < max && onAdd && (
        <IconButton icon="smile" label="Добавить эмодзи" size="sm" onClick={onAdd} />
      )}
      <span className="ss-emojis-count">
        {value.length} из {max}
      </span>
    </div>
  );
}
