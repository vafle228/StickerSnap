import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./ds/index.css";
import "./app/global.css";
import { App } from "./app/App";
import { Providers } from "./app/Providers";

const root = document.getElementById("root");
if (!root) throw new Error("#root is missing in index.html");

createRoot(root).render(
  <StrictMode>
    <Providers>
      <App />
    </Providers>
  </StrictMode>,
);
