import { useEffect, useState } from "react";

/** True once `on` has held for `ms`: a fast answer shows the list right away, without the
 *  skeleton blinking in and out. */
export function useDelayed(on: boolean, ms = 300): boolean {
  const [late, setLate] = useState(false);
  useEffect(() => {
    if (!on) {
      setLate(false);
      return undefined;
    }
    const timer = window.setTimeout(() => setLate(true), ms);
    return () => window.clearTimeout(timer);
  }, [on, ms]);
  return on && late;
}
