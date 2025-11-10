# CinéBoutique – Inventaire (PWA)

Application React 18 + Vite + TypeScript pour piloter les inventaires CinéBoutique. L'interface est pensée mobile-first (iPhone/iPad) tout en restant confortable sur desktop. Elle consomme l'API ASP.NET Core exposée sur `/api/*` et fonctionne en mode PWA (`vite-plugin-pwa` + service worker Workbox + manifest).

## Démarrage

```bash
npm install
DEV_BACKEND_ORIGIN=http://localhost:8080 npm run dev
```

Sans variable, le proxy Vite utilise automatiquement `http://localhost:8080` comme origine backend.

## Démarrage rapide (dev)

### API locale (dotnet run)
1. Lancer l’API (noter l’URL HTTP affichée, ex: `http://localhost:5255`).
2. Dans `src/inventory-web`:
   ```bash
   npm i
   npm run dev:api5255
   ```

La variable `VITE_API_BASE` contrôle l'URL de l'API :

- `.env` → valeur par défaut `/api` (reverse proxy local ou prod).
- `.env.mobile` → exemple pour tests iPhone sur le LAN (`http://<IP_VM>:<PORT>/api`).

En développement mobile, exécutez :

```bash
VITE_API_BASE=http://<IP_VM>:8080/api npm run dev -- --host
```

Par défaut l'API est attendue sur `http://localhost:8080`. En production conteneurisée, l'application est servie par Nginx sur `http://localhost:3000` (voir `Dockerfile`).

> ℹ️ En mode développement, si l'appel à `/api/locations` échoue (API arrêtée, migrations non appliquées, etc.), un jeu de données de démonstration est renvoyé automatiquement. Définissez `VITE_DISABLE_DEV_FIXTURES=true` pour désactiver ce fallback et remonter l'erreur brute.

## Scripts disponibles

- `npm run dev` : serveur de développement Vite avec HMR.
- `npm run dev:scan-sim` : lance une page dédiée de simulation de scan caméra (flux vidéo généré) pour tester sans appareil.
- `npm run build` : compilation TypeScript + build Vite (artifacts dans `dist/`).
- `npm run pwa:build` : build Vite seul (utile pour Lighthouse ou vérification rapide du SW).
- `npm run preview` : prévisualisation du build.
- `npm run pwa:preview` : prévisualisation HTTPS (requis par iOS pour la caméra et l'installation PWA).
- `npm run test` / `npm run test:watch` : suite de tests Vitest + Testing Library.
- `npm run lint` : vérification ESLint.

## Stack front

- React 18 + React Router 6
- TailwindCSS 3 pour le design system mobile-first
- Client HTTP maison basé sur `fetch` (timeouts, diagnostics, JSON sûr)
- `@zxing/browser` pour la lecture des codes-barres via caméra
- `@zxing/library` pour la configuration fine des formats pris en charge
- `react-swipeable` pour les interactions swipe en listes
- PWA : `vite-plugin-pwa`, manifest, service worker Workbox auto-update, offline shell

## PWA & installation

- Manifest généré via `vite-plugin-pwa` (fallback `public/manifest.webmanifest`).
- Service worker Workbox (`autoUpdate`) : `/assets/*` en cache-first, images en stale-while-revalidate, API en `NetworkOnly`.
- Offline : le shell React reste disponible hors ligne (données API non mises en cache → UI fallback).
- SPA : `navigateFallback` force le retour sur `index.html` hors API.

### iOS – Ajouter à l'écran d'accueil

1. Servir l'app en HTTPS (voir ci-dessous).
2. Ouvrir l'URL dans Safari, utiliser le bouton Partager → « Ajouter à l'écran d'accueil ».
3. Lancer l'icône installée : mode `standalone`, status bar `black-translucent`, safe-areas gérées (`env(safe-area-inset-*)`).

### HTTPS & caméra

- **Production** : déployer derrière un certificat TLS approuvé (AC interne ou publique).
- **Développement** : `npm run pwa:preview` (alias `vite preview --https`) ou proxy HTTPS local (Nginx) avec certificat installé
  sur l'appareil.
- Safari/Chrome mobile exigent `window.isSecureContext === true` pour la caméra (`getUserMedia`).

### Ressources graphiques

- Par contrainte dépôt, aucune image binaire n'est versionnée. Les icônes PWA sont encodées en Base64 directement dans `vite.config.ts`, `public/manifest.webmanifest` et `index.html`.
- Pour personnaliser : générer vos PNG (ex: `pnpm dlx @vite-pwa/assets-generator`) puis mettre à jour les constantes `ICON_180_BASE64`, `ICON_192_BASE64`, `ICON_512_BASE64` ainsi que le `href` Base64 dans `index.html`.
- Alternative : exposer des icônes via un CDN interne et mettre à jour les `src`/`href` pour pointer dessus.

## Fonctionnalités clés

- **Accueil** : vue synthétique du nombre de comptages actifs et des conflits éventuels.
- **Assistant d'inventaire** : sélection utilisateur → type de comptage (simple/double) → zone (API `/api/locations`) → vérification qu'aucune session n'est déjà ouverte.
- **Scan produit** : via caméra (getUserMedia) ou douchette Bluetooth HID (champ de saisie focus permanent). Recherche `/api/products/{ean}` et ajout manuel possible.
- **Gestion des sessions** : affichage des articles scannés, quantités modifiables, distinction des ajouts manuels.
- **Espace administration** : réservé aux utilisateurs disposant du rôle administrateur (édition des zones avec interactions swipe).
- **Panneaux coulissants** plutôt que modales intrusives pour les formulaires secondaires.
- **Page debug scan** : `npm run dev:scan-sim` simule un flux vidéo EAN-13 pour tester sans appareil photo.

## Tests

Les tests couvrent :

- l'affichage des indicateurs d'accueil,
- le workflow d'inventaire (mock API),
- l'ajout d'un produit via une saisie simulant une douchette Bluetooth.

Exécution :

```bash
npm run test
```

## Docker

Le dossier contient un `Dockerfile` multi-stage (build Node puis runtime Nginx) et un `nginx.conf` servant l'application en SPA avec proxy `/api` vers l'API ASP.NET Core.


## Scanner & capture code-barres

- Formats analysés nativement : EAN-13, EAN-8, Code 128, Code 39, ITF, QR Code.
- Safari iOS requiert impérativement HTTPS pour activer la caméra (`window.isSecureContext`).
- Le composant `BarcodeScanner` active en priorité l’API `BarcodeDetector` (quand disponible), sinon bascule vers ZXing avec hints `TRY_HARDER`.
- UI : aide progressive (timeout ~9 s), overlay de cadrage, bouton lampe si la caméra expose `torch`, fallback import photo.
- Optimisations caméra : contraintes `focusMode: continuous`, `ideal` 1280x720, recadrage central pour la détection.
- Douchette HID : un champ caché garde le focus pour capturer les séquences terminées par `Enter`.
- Limitations : iPhone SE 2020 nécessite une bonne luminosité et parfois quelques secondes pour le focus automatique. Le bouton lampe n’est disponible que sur Safari ≥ 16.4.
