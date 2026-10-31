import { z } from 'zod';

/** Config con validación zod: falla al arrancar si falta algo requerido (mismo patrón que el transcript worker). */
const intFrom = (def: number, min: number, max: number) => z.coerce.number().int().min(min).max(max).default(def);

// z.coerce.boolean() da true para cualquier string no vacío (incluido "false"); se compara el texto.
const boolFrom = (def: 'true' | 'false') =>
  z
    .string()
    .default(def)
    .transform((value) => value.trim().toLowerCase() === 'true');

const csv = (raw: string): string[] =>
  raw
    .split(',')
    .map((s) => s.trim())
    .filter((s) => s.length > 0);

export const configSchema = z
  .object({
    NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
    LOG_LEVEL: z.enum(['fatal', 'error', 'warn', 'info', 'debug', 'trace', 'silent']).default('info'),
    SERVICE_NAME: z.string().default('communication-recorder'),

    RECORDER_PORT: intFrom(5360, 1, 65_535),
    RECORDER_HOST: z.string().default('0.0.0.0'),
    // El timeline de una reunión larga (speaker.* frecuentes) supera el 1 MB por defecto de Fastify.
    RECORDER_HTTP_BODY_LIMIT_BYTES: intFrom(16 * 1024 * 1024, 1024, 256 * 1024 * 1024),

    RECORDER_RABBITMQ_URI: z.string().min(1),
    RECORDER_RABBITMQ_EXCHANGE: z.string().default('taxvision-events'),
    RECORDER_CLOUDSTORAGE_EXTERNAL_QUEUE: z.string().default('cloudstorage-external-uploads'),

    RECORDER_REDIS_URI: z.string().min(1),
    RECORDER_REDIS_KEY_PREFIX: z.string().default('recorder'),

    RECORDER_MINIO_ENDPOINT: z.string().min(1),
    RECORDER_MINIO_PORT: intFrom(9000, 1, 65_535),
    RECORDER_MINIO_USE_SSL: boolFrom('false'),
    RECORDER_MINIO_ACCESS_KEY: z.string().min(1),
    RECORDER_MINIO_SECRET_KEY: z.string().min(1),
    RECORDER_MINIO_TEMP_BUCKET: z.string().default('taxvision-temp'),
    RECORDER_MINIO_SOURCE_PREFIX: z.string().default('recording'),

    RECORDER_AUTH_JWKS_URI: z.string().url(),
    RECORDER_AUTH_ISSUER: z.string().default('TaxVision.Auth'),
    RECORDER_AUTH_AUDIENCE: z.string().default('TaxVision.Services'),
    RECORDER_AUTH_JWKS_CACHE_MAX_AGE_SECONDS: intFrom(300, 1, 86_400),
    RECORDER_ALLOWED_CLIENT_IDS: z.string().default('communication-worker'),

    RECORDER_RTP_LISTEN_IP: z.string().default('127.0.0.1'),
    RECORDER_RTP_ANNOUNCED_IP: z.string().default(''),
    RECORDER_RTP_PORT_MIN: intFrom(44_000, 1024, 65_534),
    RECORDER_RTP_PORT_MAX: intFrom(44_999, 1025, 65_535),

    RECORDER_SPOOL_DIR: z.string().default('/var/spool/communication-recorder'),
    RECORDER_FFMPEG_BIN_PATH: z.string().default('ffmpeg'),
    RECORDER_FFPROBE_BIN_PATH: z.string().default('ffprobe'),
    RECORDER_FONT_FILE: z.string().default('/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf'),
    // Fotos de perfil del slate (GET storage/avatars/{fileId}); vacío = iniciales.
    RECORDER_CLOUDSTORAGE_BASE_URL: z.string().default(''),
    RECORDER_AVATAR_TIMEOUT_MS: intFrom(3000, 100, 60_000),
    RECORDER_AVATAR_MAX_BYTES: intFrom(2_097_152, 1024, 20_971_520),

    RECORDER_MAX_ACTIVE_SESSIONS: intFrom(4, 1, 1000),
    RECORDER_COMPOSE_THREADS: intFrom(2, 1, 64),
    RECORDER_COMPOSE_NICE: intFrom(10, 0, 19),
    // Tope de pared de una composición = max(mínimo, duración × factor).
    RECORDER_COMPOSE_TIMEOUT_FACTOR: z.coerce.number().min(0.1).max(100).default(3),
    RECORDER_COMPOSE_TIMEOUT_MIN_MINUTES: intFrom(10, 1, 1440),
    RECORDER_ORPHAN_TIMEOUT_MINUTES: intFrom(30, 1, 10_080),
    // ffmpeg abre el socket UDP al leer el SDP; antes de responder se espera esto y se comprueba que siga vivo.
    RECORDER_CAPTURE_READY_DELAY_MS: intFrom(400, 0, 10_000),
    RECORDER_CAPTURE_STOP_TIMEOUT_MS: intFrom(5000, 100, 120_000),
    RECORDER_JANITOR_INTERVAL_MINUTES: intFrom(60, 1, 10_080),
    RECORDER_SHUTDOWN_DRAIN_MS: intFrom(60_000, 0, 3_600_000),

    RECORDER_RETRY_MAX_ATTEMPTS: intFrom(4, 1, 20),
    RECORDER_RETRY_BACKOFF_MS: z.string().default('1000,5000,30000'),
  })
  .refine((e) => e.RECORDER_RTP_PORT_MAX > e.RECORDER_RTP_PORT_MIN, {
    message: 'RECORDER_RTP_PORT_MAX must be greater than RECORDER_RTP_PORT_MIN',
    path: ['RECORDER_RTP_PORT_MAX'],
  });

export function loadConfig(env: NodeJS.ProcessEnv) {
  const raw = configSchema.parse(env);
  return {
    env: raw.NODE_ENV,
    serviceName: raw.SERVICE_NAME,
    logLevel: raw.LOG_LEVEL,
    http: { port: raw.RECORDER_PORT, host: raw.RECORDER_HOST, bodyLimitBytes: raw.RECORDER_HTTP_BODY_LIMIT_BYTES },
    rabbitmq: {
      uri: raw.RECORDER_RABBITMQ_URI,
      exchange: raw.RECORDER_RABBITMQ_EXCHANGE,
      externalUploadsQueue: raw.RECORDER_CLOUDSTORAGE_EXTERNAL_QUEUE,
    },
    redis: { uri: raw.RECORDER_REDIS_URI, keyPrefix: raw.RECORDER_REDIS_KEY_PREFIX },
    minio: {
      endpoint: raw.RECORDER_MINIO_ENDPOINT,
      port: raw.RECORDER_MINIO_PORT,
      useSSL: raw.RECORDER_MINIO_USE_SSL,
      accessKey: raw.RECORDER_MINIO_ACCESS_KEY,
      secretKey: raw.RECORDER_MINIO_SECRET_KEY,
      tempBucket: raw.RECORDER_MINIO_TEMP_BUCKET,
      sourcePrefix: raw.RECORDER_MINIO_SOURCE_PREFIX.replace(/^\/+|\/+$/g, ''),
    },
    auth: {
      jwksUri: raw.RECORDER_AUTH_JWKS_URI,
      issuer: raw.RECORDER_AUTH_ISSUER,
      audience: raw.RECORDER_AUTH_AUDIENCE,
      jwksCacheMaxAgeSeconds: raw.RECORDER_AUTH_JWKS_CACHE_MAX_AGE_SECONDS,
      allowedClientIds: csv(raw.RECORDER_ALLOWED_CLIENT_IDS),
    },
    rtp: {
      listenIp: raw.RECORDER_RTP_LISTEN_IP,
      announcedIp: raw.RECORDER_RTP_ANNOUNCED_IP.trim() || null,
      portMin: raw.RECORDER_RTP_PORT_MIN,
      portMax: raw.RECORDER_RTP_PORT_MAX,
    },
    spoolDir: raw.RECORDER_SPOOL_DIR,
    ffmpeg: { binPath: raw.RECORDER_FFMPEG_BIN_PATH, ffprobeBinPath: raw.RECORDER_FFPROBE_BIN_PATH },
    fontFile: raw.RECORDER_FONT_FILE,
    avatars: {
      cloudStorageBaseUrl: raw.RECORDER_CLOUDSTORAGE_BASE_URL.trim(),
      timeoutMs: raw.RECORDER_AVATAR_TIMEOUT_MS,
      maxBytes: raw.RECORDER_AVATAR_MAX_BYTES,
    },
    maxActiveSessions: raw.RECORDER_MAX_ACTIVE_SESSIONS,
    compose: {
      threads: raw.RECORDER_COMPOSE_THREADS,
      nice: raw.RECORDER_COMPOSE_NICE,
      timeoutFactor: raw.RECORDER_COMPOSE_TIMEOUT_FACTOR,
      timeoutMinMinutes: raw.RECORDER_COMPOSE_TIMEOUT_MIN_MINUTES,
    },
    orphanTimeoutMinutes: raw.RECORDER_ORPHAN_TIMEOUT_MINUTES,
    capture: { readyDelayMs: raw.RECORDER_CAPTURE_READY_DELAY_MS, stopTimeoutMs: raw.RECORDER_CAPTURE_STOP_TIMEOUT_MS },
    janitorIntervalMinutes: raw.RECORDER_JANITOR_INTERVAL_MINUTES,
    shutdownDrainMs: raw.RECORDER_SHUTDOWN_DRAIN_MS,
    retry: {
      maxAttempts: raw.RECORDER_RETRY_MAX_ATTEMPTS,
      backoffMs: raw.RECORDER_RETRY_BACKOFF_MS.split(',')
        .map((s) => Number.parseInt(s.trim(), 10))
        .filter((n) => Number.isFinite(n) && n >= 0),
    },
  };
}

export type RecorderConfig = ReturnType<typeof loadConfig>;
