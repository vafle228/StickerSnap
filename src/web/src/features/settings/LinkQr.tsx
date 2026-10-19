import { useMemo } from "react";
import { encode } from "uqr";
import styles from "./sections.module.css";

/** The link as a QR code on a white tile (artboard 18, section 4): for Telegram on the phone.
 *  Dark modules on white in either theme, so every camera reads it. */
export function LinkQr({ link, label }: { link: string; label: string }) {
  const { size, path } = useMemo(() => {
    const qr = encode(link, { ecc: "M", border: 0 });
    let d = "";
    qr.data.forEach((row, y) =>
      row.forEach((dark, x) => {
        if (dark) d += `M${x} ${y}h1v1h-1z`;
      }),
    );
    return { size: qr.size, path: d };
  }, [link]);

  return (
    <div className={styles.qr} role="img" aria-label={label}>
      <svg viewBox={`0 0 ${size} ${size}`} shapeRendering="crispEdges" aria-hidden>
        <path d={path} fill="#1f2024" />
      </svg>
    </div>
  );
}
