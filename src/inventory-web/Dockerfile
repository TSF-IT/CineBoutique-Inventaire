# syntax=docker/dockerfile:1.6
# ---- Web (build) ----
FROM node:20-bullseye-slim AS build
WORKDIR /app

# 1) Dépendances
COPY package*.json ./
RUN --mount=type=cache,id=npm-cache,target=/root/.npm \
    npm ci --no-audit --no-fund

# 2) Code
COPY . .

# 3) Sanity: versions visibles dans les logs
RUN node -v && npm -v

# 4) Build **sans** wrapper || (...) qui force des faux exit 1
#    Si le build échoue, le RUN échoue tout seul avec le bon code.
RUN --mount=type=cache,id=npm-cache,target=/root/.npm \
    npm run build -- --logLevel info

# ---- Runtime: Nginx Alpine ----
FROM nginx:1.27-alpine AS stage-1
COPY --from=build /app/dist /usr/share/nginx/html
COPY ./nginx.conf /etc/nginx/conf.d/default.conf
