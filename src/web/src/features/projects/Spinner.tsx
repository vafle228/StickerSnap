import styles from "./Spinner.module.css";

/** The ring of «Читаю файл» and «Узнаю о видео…» (artboards 3c A, 20·3·4). */
export function Spinner({ size = 44 }: { size?: number }) {
  return (
    <svg className={styles.spinner} width={size} height={size} viewBox="0 0 44 44" aria-hidden>
      <circle className={styles.track} cx="22" cy="22" r="18" />
      <path className={styles.arc} d="M22 4a18 18 0 0 1 18 18" />
    </svg>
  );
}
