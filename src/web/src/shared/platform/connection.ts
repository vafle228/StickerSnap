/** Where the local server is and how to prove we are its window. */
export interface ServerConnection {
  /** Origin of the server; the API paths of the contract already start with /api/v1 */
  baseUrl: string;
  /** One-time token: the Bearer header of every request and access_token of the hub */
  token?: string;
}

/* Stage 1a runs in the browser: the server itself (or Vite's dev proxy) serves the SPA, so the
   API is on the same origin. Electron (1b) will take the port and the token from the preload's
   window.stickersnap here, and nothing else in the SPA changes. */
export function serverConnection(): ServerConnection {
  return { baseUrl: globalThis.location?.origin ?? "http://127.0.0.1:5180" };
}
