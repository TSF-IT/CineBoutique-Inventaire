# Ressources visuelles (non versionnées)

Ce dossier documente les éléments graphiques attendus par l'application sans les inclure dans le dépôt. Ajoutez ici vos icônes ou visuels de marque uniquement pour un usage local, puis mettez-les à disposition via un CDN interne/extérieur lors du déploiement.

## Icônes PWA

Par défaut, la PWA s'appuie sur des icônes publiques hébergées par Icons8 (voir `vite.config.ts`). Pour utiliser vos propres visuels :

1. Placez vos icônes (`192x192`, `512x512`, éventuellement une version maskable) dans `src/inventory-web/public/assets` ou sur votre CDN.
2. Mettez à jour la configuration `VitePWA` dans `vite.config.ts` afin de référencer ces nouvelles URLs.
3. Vérifiez le rendu sur les plateformes mobiles (Android, iOS via Safari Add to Home) avant mise en production.

> ⚠️ Ne validez jamais ces fichiers binaires dans Git : ils doivent rester hors du contrôle de version.
