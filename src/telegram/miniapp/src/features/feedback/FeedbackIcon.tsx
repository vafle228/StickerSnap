/** The speech bubble of «Написать отзыв» (18·5). The design system has no such icon yet, so it
 *  is drawn here in the same 24-unit grid and stroke as its icons. */
export function FeedbackIcon({ size = 22 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden
    >
      <path d="M4 5h16v11H9l-5 4z" />
    </svg>
  );
}
