## ---- Web builder: Debian (glibc) ----
FROM node:20-bullseye-slim AS build
WORKDIR /app
ENV CI=true \
    NODE_ENV=production \
    NODE_OPTIONS=--max-old-space-size=4096 \
    VITE_SOURCEMAP=false

# dépendances (cache npm)
COPY package*.json ./
RUN npm ci --no-audit --no-fund

# sources
COPY . .

# build prod (log plus verbeux pour diagnostiquer si ça replante)
RUN npm run build -- --logLevel info || (echo "----- NPM DEBUG LOG -----" && \
    sh -lc 'set -e; f=$(ls -1 /root/.npm/_logs/*-debug-0.log 2>/dev/null | head -n1 || true); if [ -n "$f" ]; then cat "$f"; else echo "no npm debug log"; fi' && exit 1)

# ---- Runtime: Nginx Alpine ----
FROM nginx:1.27-alpine AS stage-1
COPY --from=build /app/dist /usr/share/nginx/html
COPY ./nginx.conf /etc/nginx/conf.d/default.conf
