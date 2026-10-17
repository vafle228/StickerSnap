/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** The demo data and the dev bar: in `vite` and `npm run build:dev`, never in the
   *  production build (vite.config.ts) */
  readonly DEV_TOOLS: boolean;
}
