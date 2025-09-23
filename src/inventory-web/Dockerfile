# ---------- build ----------
FROM node:20-alpine AS build
WORKDIR /app
ENV CI=true
ENV NODE_ENV=production
# augmente la mémoire V8 du process Node au build
ENV NODE_OPTIONS=--max-old-space-size=4096
# Vite: pas de sourcemaps en prod par défaut
ENV VITE_SOURCEMAP=false

# dépendances (cache npm)
COPY package*.json ./
RUN npm ci --no-audit --no-fund

# sources
COPY . .

# build prod (log plus verbeux pour diagnostiquer si ça replante)
RUN npm run build -- --logLevel info || (echo "----- NPM DEBUG LOG -----" && \
    if [ -f /root/.npm/_logs/*-debug-0.log ]; then cat /root/.npm/_logs/*-debug-0.log; else echo "no npm debug log"; fi && exit 1)

# ---------- stage-1 (nginx) ----------
FROM nginx:1.27-alpine AS stage-1
COPY --from=build /app/dist /usr/share/nginx/html
COPY ./nginx.conf /etc/nginx/conf.d/default.conf
