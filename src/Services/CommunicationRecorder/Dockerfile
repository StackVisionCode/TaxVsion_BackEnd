# syntax=docker/dockerfile:1.7-labs
#
# 2 stages: (1) build TS; (2) runtime con ffmpeg (captura -c copy + composición) y la fuente DejaVu
# que usa drawtext. Mismo Node fijado que el transcript worker.

# ---------- Stage 1: TS build ----------
FROM node:24.22.2-bookworm-slim AS build
WORKDIR /app
ENV NODE_ENV=production
COPY package.json package-lock.json ./
COPY tsconfig.json tsconfig.build.json ./
RUN npm ci --include=dev --no-audit --no-fund
COPY src ./src
RUN npm run build && npm prune --omit=dev --no-audit --no-fund

# ---------- Stage 2: runtime ----------
FROM node:24.22.2-bookworm-slim AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg fonts-dejavu-core ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV NODE_ENV=production
ENV LOG_LEVEL=info
ENV RECORDER_PORT=5360
ENV RECORDER_SPOOL_DIR=/var/spool/communication-recorder
ENV RECORDER_FFMPEG_BIN_PATH=ffmpeg
ENV RECORDER_FFPROBE_BIN_PATH=ffprobe
ENV RECORDER_FONT_FILE=/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf

COPY package.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist

RUN groupadd -r taxvision && useradd -r -g taxvision taxvision \
    && mkdir -p /var/spool/communication-recorder \
    && chown -R taxvision:taxvision /app /var/spool/communication-recorder
# useradd -r no crea el home; nada corre npm en runtime, pero evita escrituras en /.
ENV HOME=/app
USER taxvision

# API de control (HTTP) + rango RTP/RTCP (UDP) que recibe de mediasoup.
EXPOSE 5360
EXPOSE 44000-44999/udp

CMD ["node", "--enable-source-maps", "dist/main.js"]
