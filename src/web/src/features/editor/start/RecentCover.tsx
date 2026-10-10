import { useState } from "react";
import { StickerArt } from "../../../ds";
import styles from "./StartSlot.module.css";

/** A small cover on the checker; the placeholder art stays until a real picture loads. */
export function RecentCover({ src }: { src: string }) {
  const [shown, setShown] = useState(false);
  return (
    <span className={`ss-checker ${styles.cover}`} aria-hidden>
      {!shown && <StickerArt />}
      <img
        src={src}
        alt=""
        hidden={!shown}
        onLoad={(e) => setShown(e.currentTarget.naturalWidth > 1)}
        onError={() => setShown(false)}
      />
    </span>
  );
}
