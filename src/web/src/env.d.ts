/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The demo data, the dev bar and the showcase: in `vite` and `npm run build:dev`, never in
   *  the production build (vite.config.ts) */
  readonly DEV_TOOLS: boolean;
  /** The server `vite` proxies /api and /hubs to (STICKERSNAP_SERVER_URL); empty in a build,
   *  where the server is the page's own origin */
  readonly DEV_SERVER_URL: string;
}
