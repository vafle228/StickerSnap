/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The demo data, the dev bar and the showcase: in `vite` and `npm run build:dev`, never in
   *  the production build (vite.config.ts) */
  readonly DEV_TOOLS: boolean;
}
