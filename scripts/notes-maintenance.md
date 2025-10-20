# Notes de maintenance

## Dépendances transitives encore deprecated (harmless)
- `inflight@1.0.6` (héritée via Workbox, fuite mémoire théorique mais non utilisée directement)
- `glob@7.2.3` (héritée via Workbox, support officiel arrêté avant la v9)
- `source-map@0.8.0-beta.0` (utilisée par une chaîne de build, migration en cours côté amont)
