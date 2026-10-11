import { useEffect, useRef } from "react";
import { useEditor } from "../model/store";
import styles from "./VideoStage.module.css";

/** The draft render over the sticker (3c, D): what the export will give, in a loop. It plays
 *  and pauses with the editor's play button; the playhead stays the quick preview's. */
export function ExactVideo({ src }: { src: string }) {
  const ref = useRef<HTMLVideoElement>(null);
  const playing = useEditor((s) => s.playing);

  useEffect(() => {
    const video = ref.current;
    if (!video) return;
    if (playing) video.play().catch(() => {});
    else video.pause();
  }, [playing, src]);

  return <video ref={ref} className={styles.exact} src={src} loop muted playsInline />;
}
