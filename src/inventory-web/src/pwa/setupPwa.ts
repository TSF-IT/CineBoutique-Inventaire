import { registerSW } from "virtual:pwa-register";

export const setupPwa = () => {
  const updateSW = registerSW({
    immediate: true,
    onNeedRefresh() {
      // Active immédiatement la nouvelle version (skipWaiting + clientsClaim)
      updateSW(true);
    },
    onRegisteredSW(_url, reg) {
      // Vérif périodique : déclenche la recherche de mise à jour
      if (reg) setInterval(() => reg.update(), 30 * 60_000);
    },
  });

  // Reload “discret” quand l’appli revient au premier plan
  document.addEventListener("visibilitychange", () => {
    if (document.visibilityState === "visible") updateSW(true);
  });
};
