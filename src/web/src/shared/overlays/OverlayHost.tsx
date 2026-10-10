import { Fragment } from "react";
import { closeSlot, useOverlays } from "./overlays";

/** Mounted once at the root: the open window and the confirmation above it.
 *  Each Dialog portals itself to <body>; toasts live in NotifyHost, above both. */
export function OverlayHost() {
  const window = useOverlays((s) => s.window);
  const confirm = useOverlays((s) => s.confirm);
  return (
    <>
      {window && (
        <Fragment key={window.id}>{window.render(() => closeSlot("window", window.id))}</Fragment>
      )}
      {confirm && (
        <Fragment key={confirm.id}>
          {confirm.render(() => closeSlot("confirm", confirm.id))}
        </Fragment>
      )}
    </>
  );
}
