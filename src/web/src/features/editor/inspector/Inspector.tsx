import { ExportCard } from "./ExportCard";
import { FrameCard } from "./FrameCard";
import { SpeedLoopCard } from "./SpeedLoopCard";

/** Everything about one sticker at once, no tabs or wizards (artboard 3a). */
export function Inspector() {
  return (
    <>
      <FrameCard />
      <SpeedLoopCard />
      <ExportCard />
    </>
  );
}
