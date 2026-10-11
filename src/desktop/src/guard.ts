/* What the window may reach. The SPA is ours, but it shows names and links that came from
   files and from Telegram, so the shell checks every address it is handed. */

const EXTERNAL = new Set(["https:", "http:", "tg:", "mailto:"]);

const parse = (url: string): URL | null => {
  try {
    return new URL(url);
  } catch {
    return null;
  }
};

/** A link the system may open: a web page, a t.me or tg: link of Telegram, a mail. Never a
 *  file:, a javascript: or a custom scheme of some other app. */
export function isExternalLink(url: string): boolean {
  const parsed = parse(url);
  return parsed !== null && EXTERNAL.has(parsed.protocol);
}

/** Whether `url` is a page of the app itself, so the window may go there */
export function isAppUrl(url: string, appOrigin: string): boolean {
  return parse(url)?.origin === appOrigin;
}

/** A rendered file of the local server (GET /api/v1/files/{fileId}), the only thing «Save as»
 *  downloads, so the token never leaves for another host. */
export function isServerFile(url: string, serverOrigin: string): boolean {
  const parsed = parse(url);
  return parsed?.origin === serverOrigin && parsed.pathname.startsWith("/api/v1/files/");
}

/** The name a file is saved under: no folders, nothing Windows refuses */
export function safeFileName(name: string, fallback: string): string {
  const base = name.split(/[\\/]/).pop() ?? "";
  // eslint-disable-next-line no-control-regex
  const clean = base.replace(/[<>:"|?*\u0000-\u001f]/g, "_").replace(/^[.\s]+|[.\s]+$/g, "");
  return clean || fallback;
}
