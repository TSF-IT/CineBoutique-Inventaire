# Icônes locales (placeholder)

Ce répertoire peut accueillir vos icônes personnalisées pour la PWA (par exemple `pwa-192x192.png`, `pwa-512x512.png`, `pwa-maskable-512x512.png`).

Aucune image n'est commitée dans le dépôt :

- Déposez vos fichiers uniquement pour les environnements locaux ou de build.
- Référencez-les ensuite via un CDN privé ou public dans `vite.config.ts` pour la production.
- Excluez-les toujours des commits Git (`.gitignore`).

> Conseil : profitez de [`@vite-pwa/assets-generator`](https://github.com/vite-pwa/vite-plugin-pwa/tree/main/packages/assets-generator) pour générer automatiquement les dimensions nécessaires à partir d'un logo maître.
