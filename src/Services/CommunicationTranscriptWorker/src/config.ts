import 'dotenv/config';
import { z } from 'zod';
import { parseRecordingKinds } from './contracts/recording-kinds.js';

const boolFromString = (fallback: 'true' | 'false') =>
  z
    .string()
    .default(fallback)
    .transform((value) => value.trim().toLowerCase() === 'true');

const csvNumbers = (raw: string): number[] =>
  raw
    .split(',')
    .map((s) => Number.parseInt(s.trim(), 10))
    .filter((n) => Number.isFinite(n) && n >= 0);

/**
 * Config loader con validacion Zod — mismo patron que Communication
 * (src/infrastructure/config.ts): falla al arrancar si falta algo requerido.
 */
const rawEnv = z
  .object({
    NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
    LOG_LEVEL: z.enum(['fatal', 'error', 'warn', 'info', 'debug', 'trace']).default('info'),
    SERVICE_NAME: z.string().default('communication-transcript-worker'),

    TRANSCRIPT_WORKER_RABBITMQ_URI: z.string().min(1),
    TRANSCRIPT_WORKER_RABBITMQ_EXCHANGE: z.string().default('taxvision-events'),
    TRANSCRIPT_WORKER_RABBITMQ_QUEUE: z.string().default('communication-transcript-worker'),

    // Inbox de idempotencia — Redis, no in-memory: el worker puede correr con
    // N replicas y un Map local no se ve entre pods (mismo argumento que
    // motivo RedisDistributedLock en Communication, Fase 3). TTL acota cuanto
    // tiempo se recuerda un eventId ya procesado.
    TRANSCRIPT_WORKER_REDIS_URI: z.string().min(1),
    TRANSCRIPT_WORKER_INBOX_TTL_SECONDS: z.coerce.number().int().min(60).default(86_400),

    // M2M contra Auth — mismo mecanismo custom (POST auth/service-token) que
    // usa Signature (SignatureServiceTokenAcquirer), NO client_credentials
    // OAuth2 estandar. Ver README para el registro del client en Auth.
    TRANSCRIPT_WORKER_AUTH_BASE_URL: z.string().url(),
    TRANSCRIPT_WORKER_SERVICE_AUTH_CLIENT_ID: z.string().default('communication-transcript-worker'),
    TRANSCRIPT_WORKER_SERVICE_AUTH_CLIENT_SECRET: z.string().min(1),

    // Fase D2 — DownloadAsync (bajar la grabacion original) sigue via HTTP+M2M
    // contra CloudStorage, igual que Signature dejo su DownloadAsync intacto en la
    // Fase D1: leer de cualquier FolderType requeriria un IAM de MinIO mucho mas
    // amplio que el write scoped de abajo, y no estaba en el scope acordado.
    TRANSCRIPT_WORKER_CLOUDSTORAGE_BASE_URL: z.string().url(),

    // Fase D2 — subida del .txt del transcript directo a MinIO (reemplaza el
    // HTTP initiate/PUT/complete a CloudStorage). Credenciales propias, IAM scoped
    // a taxvision-temp/transcript/* (ver deploy/docker/minio/policies/transcript-source.json).
    TRANSCRIPT_WORKER_MINIO_ENDPOINT: z.string().min(1),
    TRANSCRIPT_WORKER_MINIO_PORT: z.coerce.number().int().default(9000),
    // z.coerce.boolean() NO parsea "true"/"false" como texto — hace Boolean(valor),
    // y CUALQUIER string no vacio (incluido literalmente "false") da true. Con
    // USE_SSL=false en .env eso dejaba useSSL:true en runtime, y el cliente MinIO
    // intentaba negociar TLS contra el puerto 9000 en HTTP plano (EPROTO "packet
    // length too long" al subir el transcript). z.string().transform compara el
    // valor real contra "true".
    TRANSCRIPT_WORKER_MINIO_USE_SSL: z
      .string()
      .default('false')
      .transform((value) => value === 'true'),
    TRANSCRIPT_WORKER_MINIO_ACCESS_KEY: z.string().min(1),
    TRANSCRIPT_WORKER_MINIO_SECRET_KEY: z.string().min(1),
    TRANSCRIPT_WORKER_MINIO_TEMP_BUCKET: z.string().default('taxvision-temp'),
    TRANSCRIPT_WORKER_MINIO_SOURCE_PREFIX: z.string().default('transcript'),

    // Cola dedicada (no el exchange fanout taxvision-events) donde CloudStorage
    // escucha SaveFileRequestedIntegrationEvent de productores externos (no
    // Wolverine) — ver Program.cs de CloudStorage, ListenToRabbitQueue(...)
    // .DefaultIncomingMessage<SaveFileRequestedIntegrationEvent>(). Publicar esto
    // al fanout compartido forzaria a CloudStorage a intentar deserializar CADA
    // evento de CADA servicio como este tipo — por eso una cola propia, ruteada
    // por nombre via el exchange default de RabbitMQ (routingKey = nombre de cola).
    TRANSCRIPT_WORKER_CLOUDSTORAGE_EXTERNAL_QUEUE: z.string().default('cloudstorage-external-uploads'),

    // whisper.cpp: ruta al binario compilado y al modelo ggml. Ambos se
    // generan en el Dockerfile (build de whisper.cpp + descarga del modelo),
    // no vienen del npm install.
    TRANSCRIPT_WORKER_WHISPER_BIN_PATH: z.string().default('/opt/whisper.cpp/build/bin/whisper-cli'),
    // INV-T01: large-v3-turbo (clase large en inglés y español, coste ≈ medium); small no sirve a
    // oficinas bilingües. Mismo default que el Dockerfile.
    TRANSCRIPT_WORKER_WHISPER_MODEL_PATH: z.string().default('/opt/whisper.cpp/models/ggml-large-v3-turbo-q5_0.bin'),
    // Silero VAD (ggml-silero-v6.2.0.bin). Vacio = VAD apagado.
    TRANSCRIPT_WORKER_WHISPER_VAD_MODEL_PATH: z.string().default(''),
    // INV-T01 R-4: margen de voz y silencio minimo del VAD. Los defaults de whisper.cpp (30/100 ms)
    // cortan el principio y el final de las palabras; el umbral queda en 0.5.
    TRANSCRIPT_WORKER_WHISPER_VAD_THRESHOLD: z.coerce.number().min(0.05).max(0.95).default(0.5),
    TRANSCRIPT_WORKER_WHISPER_VAD_SPEECH_PAD_MS: z.coerce.number().int().min(0).max(2000).default(300),
    TRANSCRIPT_WORKER_WHISPER_VAD_MIN_SILENCE_MS: z.coerce.number().int().min(0).max(5000).default(500),
    // INV-T01 R-3: el glosario va en CADA ventana de 30 s (sin esto solo condiciona la primera).
    TRANSCRIPT_WORKER_WHISPER_CARRY_INITIAL_PROMPT: boolFromString('true'),
    // Anti-repeticion/alucinacion (INV-14 §13.2): contexto acotado y umbral de entropia.
    TRANSCRIPT_WORKER_WHISPER_MAX_CONTEXT: z.coerce.number().int().min(-1).default(64),
    TRANSCRIPT_WORKER_WHISPER_ENTROPY_THOLD: z.coerce.number().min(0).default(2.6),
    TRANSCRIPT_WORKER_WHISPER_BEAM_SIZE: z.coerce.number().int().min(1).default(5),
    TRANSCRIPT_WORKER_WHISPER_SUPPRESS_NST: boolFromString('true'),
    // 0 = nucleos − 1.
    TRANSCRIPT_WORKER_WHISPER_THREADS: z.coerce.number().int().min(0).default(0),
    // Prioridad de whisper-cli (nice de Linux, 0–19): la VM es compartida y las llamadas y reuniones en
    // vivo tienen que ganarle la CPU a una transcripción que puede esperar. 0 = sin cambio.
    TRANSCRIPT_WORKER_WHISPER_NICE: z.coerce.number().int().min(0).max(19).default(10),
    // Respaldo si el evento no trae prompt (el de la oficina manda).
    TRANSCRIPT_WORKER_WHISPER_PROMPT: z.string().default(''),
    // Vacio = auto-detect por whisper.cpp. Fijalo (ej. "es", "en") si sabes
    // el idioma predominante de tu tenant base — evita el paso de deteccion.
    TRANSCRIPT_WORKER_WHISPER_LANGUAGE: z.string().default(''),
    // Idiomas que se hablan en las oficinas (coma). Con idioma `auto`, si whisper detecta otro (en clips
    // cortos confunde español con italiano o portugués) se repite con uno de estos. Vacio = cualquiera.
    TRANSCRIPT_WORKER_WHISPER_ALLOWED_LANGUAGES: z.string().default('en,es'),
    // Idioma permitido al que se lleva cada detección equivocada ("detectado:permitido", coma; `*` = el
    // resto). Medido con turbo sobre notas reales: el español corto sale como it/pt/is, el inglés se
    // reconoce con p ≈ 0,99; por eso lo desconocido va a español.
    TRANSCRIPT_WORKER_WHISPER_LANGUAGE_FALLBACKS: z
      .string()
      .default('pt:es,gl:es,it:es,ca:es,fr:es,ro:es,oc:es,la:es,nl:en,de:en,cy:en,*:es'),
    TRANSCRIPT_WORKER_TEMP_DIR: z.string().default('/tmp/transcript-worker'),

    // whisper.cpp solo lee WAV PCM 16kHz mono — ffmpeg transcodifica el
    // formato original de grabacion (webm/opus tipicamente) antes de invocar
    // whisper-cli. Se instala via apt en el Dockerfile, no via npm.
    TRANSCRIPT_WORKER_FFMPEG_BIN_PATH: z.string().default('ffmpeg'),
    // Fase Backend 8 — usado ANTES del transcode para detectar si el file
    // tiene track de audio (bug #245). Se instala junto con ffmpeg apt.
    TRANSCRIPT_WORKER_FFPROBE_BIN_PATH: z.string().default('ffprobe'),
    TRANSCRIPT_WORKER_FFPROBE_TIMEOUT_SECONDS: z.coerce.number().int().min(1).default(30),
    // Preprocesado antes de whisper (vacio = sin filtro). soxr requiere ffmpeg con libsoxr.
    // INV-T01 R-5: sin loudnorm (en una pasada es un compresor dinámico que sube el ruido de las pausas
    // y engaña al VAD) ni reducción de ruido (empeora el WER de Whisper).
    TRANSCRIPT_WORKER_AUDIO_FILTER: z.string().default('highpass=f=80,aresample=resampler=soxr'),

    // Limites (INV-12 H-1): timeout por corrida = max(minimo, maxDuration × factor);
    // duracion real > maxDuration + tolerancia => TranscriptFailed TooLong.
    TRANSCRIPT_WORKER_MIN_TIMEOUT_SECONDS: z.coerce.number().int().min(1).default(600),
    TRANSCRIPT_WORKER_DURATION_TOLERANCE_SECONDS: z.coerce.number().int().min(0).default(120),

    // Alerta de calidad: palabras/segundo fuera de rango (vacio o alucinacion).
    TRANSCRIPT_WORKER_WPS_MIN: z.coerce.number().min(0).default(0.3),
    TRANSCRIPT_WORKER_WPS_MAX: z.coerce.number().min(0).default(5),
    TRANSCRIPT_WORKER_WPS_MIN_DURATION_SECONDS: z.coerce.number().min(0).default(30),

    // Salida inline (notas de voz): tope del texto en el evento.
    TRANSCRIPT_WORKER_INLINE_TEXT_MAX_CHARS: z.coerce.number().int().min(1).default(4000),

    // Cuantos mensajes RecordingReady procesa en paralelo este proceso. 1: whisper
    // ya usa todos los hilos (-t); dos corridas a la vez solo se pisan (INV-14).
    TRANSCRIPT_WORKER_CONCURRENCY: z.coerce.number().int().min(1).max(8).default(1),

    // Fase Transcript 4 — retry con backoff SOLO para download/upload (fallas
    // transientes reales: HTTP 5xx, blips de red a MinIO). Configurable por
    // env para poder acortarlo en tests (backoff en ms reales seria
    // impractico de testear con delays de produccion) sin tocar codigo.
    // "maxAttempts=4" = intento original + 3 reintentos.
    TRANSCRIPT_WORKER_RETRY_MAX_ATTEMPTS: z.coerce.number().int().min(1).default(4),
    TRANSCRIPT_WORKER_RETRY_BACKOFF_MS: z.string().default('1000,5000,30000'),
    // File.NotAvailable: CloudStorage escanea un archivo a la vez (ClamAV), con
    // varias pistas la espera es de minutos. El ultimo backoff se repite.
    TRANSCRIPT_WORKER_NOT_AVAILABLE_RETRY_MAX_ATTEMPTS: z.coerce.number().int().min(1).default(15),
    TRANSCRIPT_WORKER_NOT_AVAILABLE_RETRY_BACKOFF_MS: z.string().default('5000,10000,20000,30000,60000'),

    // Fase Transcript 8 — servidor HTTP minimo (sin framework: este proceso no
    // tiene ninguna otra ruta) solo para exponer /metrics a Prometheus. Puerto
    // por defecto = el convencional del ecosistema prom-client para Node.
    TRANSCRIPT_WORKER_METRICS_PORT: z.coerce.number().int().min(1).max(65_535).default(9464),

    // Este worker es un pipeline generico de transcripcion (ver
    // contracts/recording-kinds.ts): lo que varia por dominio es solo el
    // contrato de mensajeria (que eventos disparan una transcripcion, en que
    // campo viene el id del dueño de la grabacion, con que eventos se publica
    // el resultado). JSON con RecordingKindMapping[] — si se deja vacio (el
    // default), se usa COMMUNICATION_RECORDING_KINDS tal cual, reproduciendo
    // el comportamiento exacto de este worker desde su version original.
    // Cualquier otro microservicio puede reusar esta misma imagen/codigo
    // desplegando su propia instancia con esta variable seteada a su propio
    // mapeo, sin tocar una linea de codigo.
    TRANSCRIPT_WORKER_RECORDING_KINDS: z.string().default(''),
  })
  .parse(process.env);

export const config = {
  env: rawEnv.NODE_ENV,
  isProduction: rawEnv.NODE_ENV === 'production',
  serviceName: rawEnv.SERVICE_NAME,
  logLevel: rawEnv.LOG_LEVEL,

  rabbitmq: {
    uri: rawEnv.TRANSCRIPT_WORKER_RABBITMQ_URI,
    exchange: rawEnv.TRANSCRIPT_WORKER_RABBITMQ_EXCHANGE,
    queue: rawEnv.TRANSCRIPT_WORKER_RABBITMQ_QUEUE,
  },

  redis: {
    uri: rawEnv.TRANSCRIPT_WORKER_REDIS_URI,
    inboxTtlSeconds: rawEnv.TRANSCRIPT_WORKER_INBOX_TTL_SECONDS,
  },

  auth: {
    baseUrl: rawEnv.TRANSCRIPT_WORKER_AUTH_BASE_URL,
    clientId: rawEnv.TRANSCRIPT_WORKER_SERVICE_AUTH_CLIENT_ID,
    clientSecret: rawEnv.TRANSCRIPT_WORKER_SERVICE_AUTH_CLIENT_SECRET,
  },

  cloudStorage: {
    baseUrl: rawEnv.TRANSCRIPT_WORKER_CLOUDSTORAGE_BASE_URL,
    externalUploadsQueue: rawEnv.TRANSCRIPT_WORKER_CLOUDSTORAGE_EXTERNAL_QUEUE,
  },

  minio: {
    endpoint: rawEnv.TRANSCRIPT_WORKER_MINIO_ENDPOINT,
    port: rawEnv.TRANSCRIPT_WORKER_MINIO_PORT,
    useSSL: rawEnv.TRANSCRIPT_WORKER_MINIO_USE_SSL,
    accessKey: rawEnv.TRANSCRIPT_WORKER_MINIO_ACCESS_KEY,
    secretKey: rawEnv.TRANSCRIPT_WORKER_MINIO_SECRET_KEY,
    tempBucket: rawEnv.TRANSCRIPT_WORKER_MINIO_TEMP_BUCKET,
    sourcePrefix: rawEnv.TRANSCRIPT_WORKER_MINIO_SOURCE_PREFIX,
  },

  whisper: {
    binPath: rawEnv.TRANSCRIPT_WORKER_WHISPER_BIN_PATH,
    modelPath: rawEnv.TRANSCRIPT_WORKER_WHISPER_MODEL_PATH,
    language: rawEnv.TRANSCRIPT_WORKER_WHISPER_LANGUAGE || null,
    allowedLanguages: rawEnv.TRANSCRIPT_WORKER_WHISPER_ALLOWED_LANGUAGES.split(',')
      .map((code) => code.trim().toLowerCase())
      .filter((code) => code.length > 0 && code !== 'auto'),
    languageFallbacks: parseLanguageFallbacks(rawEnv.TRANSCRIPT_WORKER_WHISPER_LANGUAGE_FALLBACKS),
    prompt: rawEnv.TRANSCRIPT_WORKER_WHISPER_PROMPT || null,
    vadModelPath: rawEnv.TRANSCRIPT_WORKER_WHISPER_VAD_MODEL_PATH.trim() || null,
    vad: {
      threshold: rawEnv.TRANSCRIPT_WORKER_WHISPER_VAD_THRESHOLD,
      speechPadMs: rawEnv.TRANSCRIPT_WORKER_WHISPER_VAD_SPEECH_PAD_MS,
      minSilenceMs: rawEnv.TRANSCRIPT_WORKER_WHISPER_VAD_MIN_SILENCE_MS,
    },
    carryInitialPrompt: rawEnv.TRANSCRIPT_WORKER_WHISPER_CARRY_INITIAL_PROMPT,
    nice: rawEnv.TRANSCRIPT_WORKER_WHISPER_NICE,
    maxContext: rawEnv.TRANSCRIPT_WORKER_WHISPER_MAX_CONTEXT,
    entropyThold: rawEnv.TRANSCRIPT_WORKER_WHISPER_ENTROPY_THOLD,
    beamSize: rawEnv.TRANSCRIPT_WORKER_WHISPER_BEAM_SIZE,
    suppressNst: rawEnv.TRANSCRIPT_WORKER_WHISPER_SUPPRESS_NST,
    threads: rawEnv.TRANSCRIPT_WORKER_WHISPER_THREADS,
    tempDir: rawEnv.TRANSCRIPT_WORKER_TEMP_DIR,
  },

  ffmpeg: {
    binPath: rawEnv.TRANSCRIPT_WORKER_FFMPEG_BIN_PATH,
    ffprobeBinPath: rawEnv.TRANSCRIPT_WORKER_FFPROBE_BIN_PATH,
    ffprobeTimeoutMs: rawEnv.TRANSCRIPT_WORKER_FFPROBE_TIMEOUT_SECONDS * 1000,
    audioFilter: rawEnv.TRANSCRIPT_WORKER_AUDIO_FILTER.trim(),
  },

  limits: {
    minTimeoutSeconds: rawEnv.TRANSCRIPT_WORKER_MIN_TIMEOUT_SECONDS,
    durationToleranceSeconds: rawEnv.TRANSCRIPT_WORKER_DURATION_TOLERANCE_SECONDS,
  },

  quality: {
    wpsMin: rawEnv.TRANSCRIPT_WORKER_WPS_MIN,
    wpsMax: rawEnv.TRANSCRIPT_WORKER_WPS_MAX,
    wpsMinDurationSeconds: rawEnv.TRANSCRIPT_WORKER_WPS_MIN_DURATION_SECONDS,
  },

  inline: {
    maxChars: rawEnv.TRANSCRIPT_WORKER_INLINE_TEXT_MAX_CHARS,
  },

  concurrency: rawEnv.TRANSCRIPT_WORKER_CONCURRENCY,

  retry: {
    maxAttempts: rawEnv.TRANSCRIPT_WORKER_RETRY_MAX_ATTEMPTS,
    backoffMs: csvNumbers(rawEnv.TRANSCRIPT_WORKER_RETRY_BACKOFF_MS),
    notAvailable: {
      maxAttempts: rawEnv.TRANSCRIPT_WORKER_NOT_AVAILABLE_RETRY_MAX_ATTEMPTS,
      backoffMs: csvNumbers(rawEnv.TRANSCRIPT_WORKER_NOT_AVAILABLE_RETRY_BACKOFF_MS),
    },
  },

  metrics: {
    port: rawEnv.TRANSCRIPT_WORKER_METRICS_PORT,
  },

  recordingKinds: parseRecordingKinds(rawEnv.TRANSCRIPT_WORKER_RECORDING_KINDS),
} as const;

export type AppConfig = typeof config;

/** "pt:es,it:es" → { pt: 'es', it: 'es' }; las entradas mal escritas se ignoran. */
export function parseLanguageFallbacks(raw: string): Readonly<Record<string, string>> {
  const map: Record<string, string> = {};
  for (const entry of raw.split(',')) {
    const [from, to] = entry.split(':').map((code) => code?.trim().toLowerCase());
    if (from && to) map[from] = to;
  }
  return map;
}

