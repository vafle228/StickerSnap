/* The last steps of the interface before an error (artboard 19, section 4): sections, pressed
   buttons and API requests, never what a field holds. A bug's report carries them (contract,
   FeedbackRequest.actions). */

export interface TrailStep {
  /** ISO time */
  at: string;
  /** «Раздел: Проекты», «Кнопка: Экспорт», «POST /api/v1/exports → 202» */
  what: string;
}

/** The report takes the last 30 steps, each up to 200 characters (contract). */
export const TRAIL_LIMIT = 30;
export const STEP_MAX = 200;

let steps: TrailStep[] = [];

/** The same step again right after itself (a poll, a double click) is one step with a count. */
const REPEAT = / \(×(\d+)\)$/;

export const trail = {
  record(what: string, at: Date = new Date()) {
    const text = what.replace(/\s+/g, " ").trim().slice(0, STEP_MAX);
    if (!text) return;
    const last = steps.at(-1);
    if (last && last.what.replace(REPEAT, "") === text) {
      const times = Number(REPEAT.exec(last.what)?.[1] ?? 1) + 1;
      steps[steps.length - 1] = { at: at.toISOString(), what: `${text} (×${times})` };
      return;
    }
    steps = [...steps, { at: at.toISOString(), what: text }].slice(-TRAIL_LIMIT);
  },

  /** The steps so far, the oldest first. */
  list(): TrailStep[] {
    return [...steps];
  },

  clear() {
    steps = [];
  },
};

/** What can be pressed, and how the trail calls it. */
const PRESSABLE: [selector: string, kind: string][] = [
  ["[role=menuitem], [role=menuitemradio], [role=option]", "Меню"],
  ["[role=tab]", "Вкладка"],
  ["input[type=checkbox], [role=checkbox], [role=switch]", "Переключатель"],
  ["a[href]", "Ссылка"],
  ["button, [role=button]", "Кнопка"],
];

/** The words a control is known by: its label, never a value someone typed. */
function nameOf(element: Element): string {
  const own =
    element.getAttribute("aria-label") ??
    element.getAttribute("title") ??
    (element instanceof HTMLInputElement ? element.closest("label")?.textContent : null) ??
    element.textContent;
  return (own ?? "").replace(/\s+/g, " ").trim().slice(0, 60);
}

/** «Кнопка: Экспорт» for a click on something that can be pressed; undefined for the rest. */
export function describeClick(target: EventTarget | null): string | undefined {
  if (!(target instanceof Element)) return undefined;
  for (const [selector, kind] of PRESSABLE) {
    const element = target.closest(selector);
    if (!element) continue;
    const name = nameOf(element);
    return name ? `${kind}: ${name}` : kind;
  }
  return undefined;
}

/** «GET /api/v1/projects/{id}» as the request went, without its query: a query may hold a name. */
export function describeRequest(method: string, url: string, status: number | string): string {
  let path = url;
  try {
    path = new URL(url, "http://local").pathname;
  } catch {
    // Not a URL: the path as it is
  }
  return `${method.toUpperCase()} ${path} → ${status}`;
}
