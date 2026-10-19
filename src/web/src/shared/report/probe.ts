/* The demo's way to drop a screen (scenario «Экран упал»): armed by the mocks only, the next
   section that draws throws once and shows 19·3. Never armed outside the demo. */

export const crashProbe = { armed: false };

/** Rendered inside a section's boundary: throws while armed. React draws a failed screen again
 *  at once before it gives up, so the probe disarms only after this task: «Перезапустить экран»
 *  then draws the section. */
export function CrashProbe() {
  if (crashProbe.armed) {
    setTimeout(() => (crashProbe.armed = false), 0);
    throw new TypeError("Cannot read properties of undefined (reading 'segments')");
  }
  return null;
}
