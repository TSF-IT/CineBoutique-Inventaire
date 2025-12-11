# Déploiement prod (overlay)

## Fichiers
- `deploy/prod/docker-compose.prod.yml` : overrides prod
- `deploy/prod/nginx/conf.d/site.conf` : conf Nginx (PWA + proxy API)

## Runbook (extrait)
```bash
cd /home/admintsf/apps/inventaire
# mettre à jour le code
git fetch --all --prune && git pull --ff-only

# appliquer les configs prod
cp deploy/prod/docker-compose.prod.yml .
mkdir -p nginx/conf.d
cp deploy/prod/nginx/conf.d/site.conf nginx/conf.d/

# redéploiement
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d web api
docker compose -f docker-compose.yml -f docker-compose.prod.yml ps


