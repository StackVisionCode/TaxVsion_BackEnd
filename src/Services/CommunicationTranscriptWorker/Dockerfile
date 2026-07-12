# syntax=docker/dockerfile:1.7-labs
#
# 3 stages: (1) compila whisper.cpp desde fuente — deliberadamente NO un
# binario npm-wrapped, para no arrastrar Python al stack (decision Fase 6,
# whisper.cpp sobre faster-whisper); (2) build TS del worker; (3) runtime
# minimo con ffmpeg + el binario/modelo de whisper.cpp copiados.

# ---------- Stage 1: whisper.cpp build ----------
FROM debian:bookworm-slim AS whisper-build
RUN apt-get update && apt-get install -y --no-install-recommends \
    git cmake build-essential ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt
# Pin a un tag estable — actualizar deliberadamente, no seguir master.
RUN git clone --depth 1 --branch v1.7.2 https://github.com/ggerganov/whisper.cpp.git whisper.cpp
WORKDIR /opt/whisper.cpp
RUN cmake -B build -DCMAKE_BUILD_TYPE=Release -DWHISPER_BUILD_EXAMPLES=ON -DWHISPER_BUILD_TESTS=OFF \
    && cmake --build build --config Release -j"$(nproc)"

# Modelo por defecto: ggml-base (multilingue, ~142MB). Cambiar via
# TRANSCRIPT_WORKER_WHISPER_MODEL_PATH si se necesita otro tamano.
RUN mkdir -p models && curl -fL \
    -o models/ggml-base.bin \
    https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin

# ---------- Stage 2: TS build ----------
FROM node:20.11-bookworm-slim AS build
WORKDIR /app
ENV NODE_ENV=production
COPY package.json ./
COPY tsconfig.json tsconfig.build.json ./
RUN --mount=type=cache,target=/root/.npm \
    npm install --include=dev --no-audit --no-fund
COPY src ./src
RUN npm run build

# ---------- Stage 3: runtime ----------
FROM node:20.11-bookworm-slim AS runtime
WORKDIR /app

# ffmpeg: transcodifica la grabacion original a WAV 16kHz mono (whisper.cpp
# no lee webm/opus). libgomp1: runtime OpenMP que usa whisper.cpp para
# threading. curl: healthcheck-friendly si se agrega uno mas adelante.
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg libgomp1 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ENV NODE_ENV=production
ENV LOG_LEVEL=info
ENV TRANSCRIPT_WORKER_WHISPER_BIN_PATH=/opt/whisper.cpp/build/bin/whisper-cli
ENV TRANSCRIPT_WORKER_WHISPER_MODEL_PATH=/opt/whisper.cpp/models/ggml-base.bin
ENV TRANSCRIPT_WORKER_TEMP_DIR=/tmp/transcript-worker

COPY --from=whisper-build /opt/whisper.cpp/build/bin/whisper-cli /opt/whisper.cpp/build/bin/whisper-cli
COPY --from=whisper-build /opt/whisper.cpp/build/src/*.so* /opt/whisper.cpp/build/src/
COPY --from=whisper-build /opt/whisper.cpp/build/ggml/src/*.so* /opt/whisper.cpp/build/ggml/src/
COPY --from=whisper-build /opt/whisper.cpp/models/ggml-base.bin /opt/whisper.cpp/models/ggml-base.bin
ENV LD_LIBRARY_PATH=/opt/whisper.cpp/build/src:/opt/whisper.cpp/build/ggml/src

COPY package.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist

RUN groupadd -r taxvision && useradd -r -g taxvision taxvision \
    && mkdir -p /tmp/transcript-worker && chown -R taxvision:taxvision /app /tmp/transcript-worker
USER taxvision

CMD ["node", "--enable-source-maps", "dist/main.js"]
