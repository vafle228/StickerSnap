/** Placeholder sticker drawn with content tints (not the UI rainbow) when there is no preview. */
export function StickerArt() {
  return (
    <svg viewBox="0 0 100 100" aria-hidden>
      <circle cx={50} cy={52} r={38} fill="var(--tint-peach)" />
      <circle cx={50} cy={52} r={38} fill="none" stroke="var(--checker-a)" strokeWidth={5} />
      <circle cx={38} cy={46} r={5} fill="var(--on-prism)" />
      <circle cx={62} cy={46} r={5} fill="var(--on-prism)" />
      <path
        d="M36 62q14 12 28 0"
        fill="none"
        stroke="var(--on-prism)"
        strokeWidth={5}
        strokeLinecap="round"
      />
    </svg>
  );
}
