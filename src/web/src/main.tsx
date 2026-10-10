import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./ds/index.css";
import "./app/global.css";
import { App } from "./app/App";
import { Providers } from "./app/Providers";

const root = document.getElementById("root");
if (!root) throw new Error("#root is missing in index.html");

// Dev only: /?showcase opens the design system showcase. The branch is dropped from production builds.
if (import.meta.env.DEV && new URLSearchParams(location.search).has("showcase")) {
  void import("./showcase/Showcase").then(({ Showcase }) =>
    createRoot(root).render(
      <StrictMode>
        <Showcase />
      </StrictMode>,
    ),
  );
} else {
  createRoot(root).render(
    <StrictMode>
      <Providers>
        <App />
      </Providers>
    </StrictMode>,
  );
}
