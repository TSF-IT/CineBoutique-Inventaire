import { StrictMode } from "react";
import { createRoot } from "react-dom/client";

import "./index.css";
import "./styles/util-classes.css";
import { App } from "./App";
import { initializeTheme } from "./app/utils/theme";
import { ShopProvider } from "./state/ShopContext";
import { setupPwa } from "./pwa/setupPwa";

initializeTheme();

const root = createRoot(document.getElementById("root")!);

if (import.meta.env.PROD) {
  setupPwa();
}

root.render(
  <StrictMode>
    <ShopProvider>
      <App />
    </ShopProvider>
  </StrictMode>
);
