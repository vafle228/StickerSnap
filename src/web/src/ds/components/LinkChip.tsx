import { useEffect, useState } from "react";
import { cx } from "../cx";
import { Icon } from "./Icon";

export interface LinkChipProps {
  /** t.me/addstickers/… */
  href: string;
  label?: string;
  onCopy?: (href: string) => void;
  className?: string;
}

/** Pack link: a click copies it and says «Скопировано» in place for 2 s, no toast. */
export function LinkChip({ href, label, onCopy, className }: LinkChipProps) {
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const copy = () => {
    navigator.clipboard?.writeText(href).catch(() => {
      /* clipboard blocked: the chip still confirms, the link is visible to copy by hand */
    });
    setCopied(true);
    onCopy?.(href);
  };

  return (
    <button
      type="button"
      className={cx("ss-linkchip", copied && "is-copied", className)}
      title="Скопировать ссылку"
      aria-live="polite"
      onClick={copy}
    >
      <Icon name={copied ? "check" : "link"} size={16} />
      <span>{copied ? "Скопировано" : (label ?? href.replace(/^https?:\/\//, ""))}</span>
    </button>
  );
}
