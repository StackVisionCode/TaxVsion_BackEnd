# syntax=docker/dockerfile:1.7-labs

# ---------- Stage 1: build ----------
FROM node:20.11-bookworm-slim AS build
WORKDIR /app

ENV NODE_ENV=production
COPY package.json ./
COPY tsconfig.json tsconfig.build.json ./
COPY prisma ./prisma

RUN --mount=type=cache,target=/root/.npm \
    npm install --include=dev --no-audit --no-fund

COPY src ./src
RUN npx prisma generate
RUN npm run build

# ---------- Stage 2: runtime ----------
FROM node:20.11-bookworm-slim AS runtime
WORKDIR /app

# Herramientas para OpenSSL de Prisma + healthcheck.
RUN apt-get update && apt-get install -y --no-install-recommends \
    openssl ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

ENV NODE_ENV=production
ENV LOG_LEVEL=info
ENV COMMUNICATION_HTTP_HOST=0.0.0.0
ENV COMMUNICATION_HTTP_PORT=5350

COPY package.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist
COPY --from=build /app/prisma ./prisma

RUN groupadd -r taxvision && useradd -r -g taxvision taxvision \
    && chown -R taxvision:taxvision /app
# useradd -r no crea /home/taxvision — cualquier herramienta que escriba en
# $HOME (ej. npx en el paso de "prisma migrate deploy" del deploy, que corre
# este mismo runtime con el CMD overrideado) revienta con EACCES al no poder
# hacer mkdir ahi. /app ya es de taxvision, asi que HOME apunta ahi en vez de
# a un directorio que no existe.
ENV HOME=/app
USER taxvision

EXPOSE 5350

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -f http://localhost:5350/health/live || exit 1

CMD ["node", "--enable-source-maps", "dist/main.js"]
