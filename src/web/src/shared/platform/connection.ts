/** Where the local server is and how to prove we are its window. */
export interface ServerConnection {
  /** Origin of the server; the API paths of the contract already start with /api/v1 */
  baseUrl: string;
  /** One-time token: the Bearer header of every request and access_token of the hub */
  token?: string;
}

/* In the browser the server itself (or Vite's dev proxy) serves the SPA, so the API is on the
   same origin and needs no token. The desktop shell hands the server's origin and the launch's
   token to the preload's window.stickersnap; in its development mode over Vite it has neither. */
export function serverConnection(): ServerConnection {
  const desktop = globalThis.stickersnap?.server;
  return {
    baseUrl: desktop?.baseUrl ?? globalThis.location?.origin ?? "http://127.0.0.1:5180",
    token: desktop?.token,
  };
}
