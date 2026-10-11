import type { Placement } from "@floating-ui/react-dom";

/** Where the panel grows from: the corner that touches its button («bottom-end» → «top right»). */
export function originOf(placement: Placement): string {
  const [side, align] = placement.split("-");
  const across =
    side === "top" || side === "bottom"
      ? align === "start"
        ? "left"
        : align === "end"
          ? "right"
          : "center"
      : align === "start"
        ? "top"
        : align === "end"
          ? "bottom"
          : "center";
  const opposite = { top: "bottom", bottom: "top", left: "right", right: "left" }[side ?? "bottom"];
  return side === "top" || side === "bottom" ? `${opposite} ${across}` : `${across} ${opposite}`;
}
