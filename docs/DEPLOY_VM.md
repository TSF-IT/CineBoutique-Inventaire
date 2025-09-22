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
