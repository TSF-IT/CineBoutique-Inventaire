# Déploiement sur VM (branche `deploy` uniquement)

## Pré-requis sur la VM (Debian)
- Docker & Docker Compose Plugin installés
- Git installé
- Le repo cloné dans `~/apps/CineBoutique-Inventaire`
- L’utilisateur courant membre du groupe `docker`

## Premier setup
```bash
mkdir -p ~/apps
cd ~/apps
git clone git@github.com:<org_ou_user>/CineBoutique-Inventaire.git
cd CineBoutique-Inventaire
git checkout deploy

Script de déploiement

Le script suivant tire la branche deploy et relance l’app :

chmod +x scripts/deploy/pull_and_rebuild.sh
APP_DIR=~/apps/CineBoutique-Inventaire BRANCH=deploy ./scripts/deploy/pull_and_rebuild.sh

Systemd (déploiement périodique)

Créer le service :

~/.config/systemd/user/app-deploy.service


Contenu :

[Unit]
Description=Pull & rebuild CineBoutique from deploy branch

[Service]
Type=oneshot
Environment=APP_DIR=%h/apps/CineBoutique-Inventaire
Environment=BRANCH=deploy
ExecStart=%h/apps/CineBoutique-Inventaire/scripts/deploy/pull_and_rebuild.sh


Créer le timer :

~/.config/systemd/user/app-deploy.timer


Contenu :

[Unit]
Description=Run deploy script every 2 minutes

[Timer]
OnBootSec=30sec
OnUnitActiveSec=2min
Unit=app-deploy.service

[Install]
WantedBy=default.target


Activer côté utilisateur :

systemctl --user daemon-reload
systemctl --user enable --now app-deploy.timer
systemctl --user list-timers | grep app-deploy


Important : La VM suit uniquement la branche deploy.
Le workflow GitHub met à jour deploy uniquement si la CI (backend & frontend) est verte sur main.

## Proxy HTTPS (Caddy)

Le service `proxy` (image Caddy) termine désormais TLS et route :

- `/api` vers l’API .NET (service `api`)
- toutes les autres routes vers le front nginx (service `web`)

Deux modes sont possibles :

1. **Domaine public** : définir les variables d’environnement `DOMAIN` (ex. `inventaire.mondomaine.fr`) et éventuellement `ACME_EMAIL` dans `docker-compose.yml` ou via `DOMAIN=... ACME_EMAIL=... docker compose up -d`. Caddy utilisera Let’s Encrypt automatiquement. Le port 80 redirige vers 443.
2. **Certificat interne** : ne pas définir `DOMAIN` et déposer un couple `cert.pem` / `key.pem` dans `./certs`. Ils sont montés en lecture seule dans le conteneur. Caddy refusera de démarrer si les fichiers manquent.

### Cas iPhone / iPad en réseau interne

Les appareils iOS exigent un certificat approuvé pour exposer `navigator.mediaDevices`.

1. Générez un certificat racine ou utilisez votre PKI interne pour créer `cert.pem` / `key.pem`.
2. Copiez `cert.pem` sur l’iPhone (AirDrop, Mail…).
3. Ouvrez le fichier sur iOS, installez le profil et activez-le dans **Réglages > Général > Informations > Réglages de confiance du certificat**.
4. Ouvrez ensuite l’application via l’URL en `https://` pour débloquer la caméra.

> Astuce : pour des tests rapides sans certificat, vous pouvez utiliser le fichier `src/inventory-web/.env.mobile` afin de pointer directement vers l’API en HTTP. Attention : le scan caméra ne fonctionnera pas, seul le fallback « import de photo » restera disponible.
