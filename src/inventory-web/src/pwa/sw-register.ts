import { registerSW } from 'virtual:pwa-register'

export const enablePwa = () => {
  registerSW({
    onNeedRefresh() {
      // TODO: afficher un toast / bannière si nécessaire
    },
    onOfflineReady() {
      // TODO: notifier l'utilisateur que le cache est prêt
    },
  })
}
