# CinéBoutique – Inventaire (PWA)

Application React 18 + Vite + TypeScript pour piloter les inventaires CinéBoutique. L'interface est pensée mobile-first (iPhone/iPad) tout en restant confortable sur desktop. Elle consomme l'API ASP.NET Core exposée sur `/api/*` et fonctionne en mode PWA (manifest + service worker via `vite-plugin-pwa`).

## Démarrage

```bash
npm install
DEV_BACKEND_ORIGIN=http://localhost:8080 npm run dev
```

Sans variable, le proxy Vite utilise automatiquement `http://localhost:8080` comme origine backend.

La variable `VITE_API_BASE` contrôle l'URL de l'API :

- `.env` → valeur par défaut `/api` (reverse proxy local ou prod).
- `.env.mobile` → exemple pour tests iPhone sur le LAN (`http://<IP_VM>:<PORT>/api`).

En développement mobile, exécutez :

```bash
VITE_API_BASE=http://<IP_VM>:8080/api npm run dev -- --host
```

Par défaut l'API est attendue sur `http://localhost:8080`. En production conteneurisée, l'application est servie par Nginx sur `http://localhost:3000` (voir `Dockerfile`).

## Scripts disponibles

- `npm run dev` : serveur de développement Vite avec HMR.
- `npm run dev:scan-sim` : lance une page dédiée de simulation de scan caméra (flux vidéo généré) pour tester sans appareil.
- `npm run build` : compilation TypeScript + build Vite (artifacts dans `dist/`).
- `npm run preview` : prévisualisation du build.
- `npm run test` / `npm run test:watch` : suite de tests Vitest + Testing Library.
- `npm run lint` : vérification ESLint.

## Stack front

- React 18 + React Router 6
- TailwindCSS 3 pour le design system mobile-first
- Client HTTP maison basé sur `fetch` (timeouts, diagnostics, JSON sûr)
- `@zxing/browser` pour la lecture des codes-barres via caméra
- `@zxing/library` pour la configuration fine des formats pris en charge
- `react-swipeable` pour les interactions swipe en listes
- PWA : `vite-plugin-pwa`, manifest, icônes servies via CDN (personnalisables), service worker auto-update

## Fonctionnalités clés

- **Accueil** : vue synthétique du nombre de comptages actifs et des conflits éventuels.
- **Assistant d'inventaire** : sélection utilisateur → type de comptage (simple/double) → zone (API `/api/locations`) → vérification qu'aucune session n'est déjà ouverte.
- **Scan produit** : via caméra (getUserMedia) ou douchette Bluetooth HID (champ de saisie focus permanent). Recherche `/api/products/{ean}` et ajout manuel possible.
- **Gestion des sessions** : affichage des articles scannés, quantités modifiables, distinction des ajouts manuels.
- **Espace administrateur** : authentification simple (JWT côté API), CRUD des zones avec interactions swipe.
- **Panneaux coulissants** plutôt que modales intrusives pour les formulaires secondaires.

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

## Ressources graphiques

Le dépôt ne contient aucune image (icône, logo, etc.) afin de rester 100 % texte. Pour personnaliser l'identité visuelle :

1. Consultez `assets/README.md` pour comprendre comment intégrer des visuels locaux sans les versionner.
2. Ajoutez vos fichiers dans `public/assets/` (ignoré par Git) ou servez-les depuis un CDN interne.
3. Adaptez ensuite `vite.config.ts` pour pointer vers vos propres URLs.

Par défaut, la PWA référence des icônes publiques Icons8 qui garantissent un fonctionnement immédiat sans ressources binaires dans le repository.
- **Page debug scan** : commande `npm run dev:scan-sim` qui remplace `getUserMedia` par un flux vidéo simulé d’EAN-13 pour tester la détection sur desktop.

## Scanner & capture code-barres

- Formats analysés nativement : EAN-13, EAN-8, Code 128, Code 39, ITF, QR Code.
- Safari iOS requiert impérativement HTTPS pour activer la caméra (`window.isSecureContext`).
- Le composant `BarcodeScanner` active en priorité l’API `BarcodeDetector` (quand disponible), sinon bascule vers ZXing avec hints `TRY_HARDER`.
- UI : aide progressive (timeout ~9 s), overlay de cadrage, bouton lampe si la caméra expose `torch`, fallback import photo.
- Optimisations caméra : contraintes `focusMode: continuous`, `ideal` 1280x720, recadrage central pour la détection.
- Douchette HID : un champ caché garde le focus pour capturer les séquences terminées par `Enter`.
- Limitations : iPhone SE 2020 nécessite une bonne luminosité et parfois quelques secondes pour le focus automatique. Le bouton lampe n’est disponible que sur Safari ≥ 16.4.
