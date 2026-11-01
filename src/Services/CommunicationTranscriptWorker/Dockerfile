# syntax=docker/dockerfile:1.7-labs
#
# 3 stages: (1) compila whisper.cpp desde fuente — deliberadamente NO un
# binario npm-wrapped, para no arrastrar Python al stack (decision Fase 6,
# whisper.cpp sobre faster-whisper) — y descarga los modelos; (2) build TS del
# worker; (3) runtime minimo con ffmpeg + whisper-cli + librerias + modelos.

# ---------- Stage 1: whisper.cpp build ----------
FROM debian:bookworm-slim AS whisper-build
RUN apt-get update && apt-get install -y --no-install-recommends \
    git cmake build-essential ca-certificates curl \
    && rm -rf /var/lib/apt/lists/*

# D-26: misma version que corre en local (whisper-cli --version = 1.9.2):
# VAD Silero, flash-attention por defecto, -sns. Actualizar deliberadamente.
ARG WHISPER_VERSION=v1.9.2
# Vacio = automatico por arquitectura (ver abajo). Permite fijar flags tras T-29.
ARG WHISPER_CPU_CMAKE_FLAGS=""
# arm64 sin variantes dinamicas: GCC 12 (bookworm) no conoce +sme de las
# variantes armv9.2. armv8.2 + dotprod + fp16 cubre Neoverse N1 (Ampere/Graviton2).
ARG WHISPER_ARM64_ARCH=armv8.2-a+dotprod+fp16

WORKDIR /opt
RUN git clone --depth 1 --branch "${WHISPER_VERSION}" https://github.com/ggml-org/whisper.cpp.git whisper.cpp
WORKDIR /opt/whisper.cpp
# x86_64: GGML_BACKEND_DL + GGML_CPU_ALL_VARIANTS => libggml-cpu-<variante>.so
# (haswell, skylakex, alderlake...) y ggml elige en runtime la mejor para el
# CPU real; NATIVE=OFF evita atar la imagen al CPU de la maquina que la compila.
# En 1.9.x todas las .so (whisper, ggml, ggml-base, ggml-cpu-*) quedan en build/bin.
RUN set -eux; \
    if [ -n "${WHISPER_CPU_CMAKE_FLAGS}" ]; then CPU_FLAGS="${WHISPER_CPU_CMAKE_FLAGS}"; \
    else case "$(uname -m)" in \
      x86_64) CPU_FLAGS="-DGGML_NATIVE=OFF -DGGML_BACKEND_DL=ON -DGGML_CPU_ALL_VARIANTS=ON" ;; \
      aarch64) CPU_FLAGS="-DGGML_NATIVE=OFF -DGGML_CPU_ARM_ARCH=${WHISPER_ARM64_ARCH}" ;; \
      *) CPU_FLAGS="-DGGML_NATIVE=OFF" ;; \
    esac; fi; \
    cmake -B build -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=ON \
      -DWHISPER_BUILD_EXAMPLES=ON -DWHISPER_BUILD_TESTS=OFF -DWHISPER_BUILD_SERVER=OFF ${CPU_FLAGS}; \
    cmake --build build --config Release -j"$(nproc)" --target whisper-cli whisper-bench; \
    mkdir -p /opt/whisper-dist/bin; \
    cp -a build/bin/whisper-cli build/bin/whisper-bench build/bin/*.so* /opt/whisper-dist/bin/; \
    ls -la /opt/whisper-dist/bin

# Modelos por build-arg. INV-T01: large-v3-turbo-q5_0 (574 MB, ~1.0–1.4 GB de RAM pico): clase large
# en inglés y español; small no sirve a oficinas bilingües. SHA-256 = X-Linked-ETag de Hugging Face;
# al cambiar el modelo pasar su sha o "" para no verificar.
ARG WHISPER_MODEL=ggml-large-v3-turbo-q5_0.bin
ARG WHISPER_MODEL_SHA256=394221709cd5ad1f40c46e6031ca61bce88931e6e088c188294c6d5a55ffa7e2
ARG WHISPER_MODEL_BASE_URL=https://huggingface.co/ggerganov/whisper.cpp/resolve/main
ARG WHISPER_VAD_MODEL=ggml-silero-v6.2.0.bin
ARG WHISPER_VAD_MODEL_SHA256=2aa269b785eeb53a82983a20501ddf7c1d9c48e33ab63a41391ac6c9f7fb6987
ARG WHISPER_VAD_MODEL_BASE_URL=https://huggingface.co/ggml-org/whisper-vad/resolve/main
RUN set -eux; \
    mkdir -p /opt/whisper-dist/models; cd /opt/whisper-dist/models; \
    curl -fL --retry 3 -o "${WHISPER_MODEL}" "${WHISPER_MODEL_BASE_URL}/${WHISPER_MODEL}"; \
    curl -fL --retry 3 -o "${WHISPER_VAD_MODEL}" "${WHISPER_VAD_MODEL_BASE_URL}/${WHISPER_VAD_MODEL}"; \
    if [ -n "${WHISPER_MODEL_SHA256}" ]; then echo "${WHISPER_MODEL_SHA256}  ${WHISPER_MODEL}" | sha256sum -c -; fi; \
    if [ -n "${WHISPER_VAD_MODEL_SHA256}" ]; then echo "${WHISPER_VAD_MODEL_SHA256}  ${WHISPER_VAD_MODEL}" | sha256sum -c -; fi

# ---------- Stage 2: TS build ----------
# Node 24 LTS fijado al patch; bookworm igual que whisper-build (misma glibc/libgomp).
FROM node:24.22.2-bookworm-slim AS build
WORKDIR /app
ENV NODE_ENV=production
COPY package.json package-lock.json ./
COPY tsconfig.json tsconfig.build.json ./
RUN npm ci --include=dev --no-audit --no-fund
COPY src ./src
RUN npm run build

# ---------- Stage 3: runtime ----------
FROM node:24.22.2-bookworm-slim AS runtime
WORKDIR /app

# ffmpeg: preprocesado + transcode a WAV 16 kHz mono (el de Debian trae libsoxr,
# que usa el filtro por defecto aresample=resampler=soxr). libgomp1: OpenMP de ggml.
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg libgomp1 ca-certificates \
    && rm -rf /var/lib/apt/lists/*

ARG WHISPER_MODEL=ggml-large-v3-turbo-q5_0.bin
ARG WHISPER_VAD_MODEL=ggml-silero-v6.2.0.bin

ENV NODE_ENV=production
ENV LOG_LEVEL=info
ENV TRANSCRIPT_WORKER_WHISPER_BIN_PATH=/opt/whisper.cpp/build/bin/whisper-cli
ENV TRANSCRIPT_WORKER_WHISPER_MODEL_PATH=/opt/whisper.cpp/models/${WHISPER_MODEL}
# VAD encendido en la imagen; para apagarlo, TRANSCRIPT_WORKER_WHISPER_VAD_MODEL_PATH="" en runtime.
ENV TRANSCRIPT_WORKER_WHISPER_VAD_MODEL_PATH=/opt/whisper.cpp/models/${WHISPER_VAD_MODEL}
ENV TRANSCRIPT_WORKER_TEMP_DIR=/tmp/transcript-worker

# Misma ruta que en el build: el RPATH de whisper-cli apunta a build/bin y ggml
# busca los backends ggml-cpu-*.so junto al ejecutable. LD_LIBRARY_PATH como respaldo.
COPY --from=whisper-build /opt/whisper-dist/bin/ /opt/whisper.cpp/build/bin/
COPY --from=whisper-build /opt/whisper-dist/models/ /opt/whisper.cpp/models/
ENV LD_LIBRARY_PATH=/opt/whisper.cpp/build/bin

COPY package.json ./
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/dist ./dist

RUN groupadd -r taxvision && useradd -r -g taxvision taxvision \
    && mkdir -p /tmp/transcript-worker && chown -R taxvision:taxvision /app /tmp/transcript-worker
# useradd -r no crea /home/taxvision — mismo bug que en Communication/Dockerfile,
# mismo fix preventivo (nada corre npm en runtime hoy, pero cualquier `docker
# compose run` con un comando distinto al CMD default lo pisaria igual).
ENV HOME=/app
USER taxvision

# Fase Transcript 8 — servidor HTTP minimo solo para /metrics (Prometheus scrape).
EXPOSE 9464

CMD ["node", "--enable-source-maps", "dist/main.js"]
