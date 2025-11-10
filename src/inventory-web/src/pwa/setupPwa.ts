import { registerSW } from "virtual:pwa-register";

export const setupPwa = () => {
  let registration: ServiceWorkerRegistration | undefined;

  const requestUpdate = () => {
    if (registration) return registration.update();
    if (!("serviceWorker" in navigator)) return;

    return navigator.serviceWorker.getRegistration().then((reg) => {
      if (reg) {
        registration = reg;
        return reg.update();
      }
    });
  };

  registerSW({
    immediate: true,
    onRegisteredSW(_url, reg) {
      registration = reg;
      requestUpdate();

      if (reg) {
        // Vérif périodique pour éviter que Safari iOS garde une version figée
        setInterval(() => reg.update(), 30 * 60_000);
      }
    },
  });

  const handleVisibilityChange = () => {
    if (document.visibilityState === "visible") {
      requestUpdate();
    }
  };

  document.addEventListener("visibilitychange", handleVisibilityChange);
  window.addEventListener("focus", requestUpdate);

  if ("serviceWorker" in navigator) {
    navigator.serviceWorker.addEventListener("controllerchange", () => {
      // Laisse le nouveau SW prendre totalement le contrôle avant reload
      setTimeout(() => window.location.reload(), 150);
    });
  }
};
