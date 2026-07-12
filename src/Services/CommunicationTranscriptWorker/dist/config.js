import 'dotenv/config';
import { z } from 'zod';
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
    TRANSCRIPT_WORKER_CLOUDSTORAGE_BASE_URL: z.string().url(),
    // whisper.cpp: ruta al binario compilado y al modelo ggml. Ambos se
    // generan en el Dockerfile (build de whisper.cpp + descarga del modelo),
    // no vienen del npm install.
    TRANSCRIPT_WORKER_WHISPER_BIN_PATH: z.string().default('/opt/whisper.cpp/build/bin/whisper-cli'),
    TRANSCRIPT_WORKER_WHISPER_MODEL_PATH: z.string().default('/opt/whisper.cpp/models/ggml-base.bin'),
    // Vacio = auto-detect por whisper.cpp. Fijalo (ej. "es", "en") si sabes
    // el idioma predominante de tu tenant base — evita el paso de deteccion.
    TRANSCRIPT_WORKER_WHISPER_LANGUAGE: z.string().default(''),
    TRANSCRIPT_WORKER_TEMP_DIR: z.string().default('/tmp/transcript-worker'),
    // whisper.cpp solo lee WAV PCM 16kHz mono — ffmpeg transcodifica el
    // formato original de grabacion (webm/opus tipicamente) antes de invocar
    // whisper-cli. Se instala via apt en el Dockerfile, no via npm.
    TRANSCRIPT_WORKER_FFMPEG_BIN_PATH: z.string().default('ffmpeg'),
    // Cuantos mensajes RecordingReady procesa en paralelo este proceso — bajo
    // a proposito, whisper es CPU-bound y no queremos saturar el pod.
    TRANSCRIPT_WORKER_CONCURRENCY: z.coerce.number().int().min(1).max(8).default(2),
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
    },
    whisper: {
        binPath: rawEnv.TRANSCRIPT_WORKER_WHISPER_BIN_PATH,
        modelPath: rawEnv.TRANSCRIPT_WORKER_WHISPER_MODEL_PATH,
        language: rawEnv.TRANSCRIPT_WORKER_WHISPER_LANGUAGE || null,
        tempDir: rawEnv.TRANSCRIPT_WORKER_TEMP_DIR,
    },
    ffmpeg: {
        binPath: rawEnv.TRANSCRIPT_WORKER_FFMPEG_BIN_PATH,
    },
    concurrency: rawEnv.TRANSCRIPT_WORKER_CONCURRENCY,
};
//# sourceMappingURL=config.js.map