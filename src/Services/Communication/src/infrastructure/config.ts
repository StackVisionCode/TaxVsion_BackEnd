import 'dotenv/config';
import { z } from 'zod';

/**
 * Config loader con validacion Zod. Falla al arrancar si falta cualquier variable
 * requerida — misma filosofia que el resto del backend (fail-fast en boot).
 * No exportamos process.env directo; todo pasa por este objeto tipado.
 */
const rawEnv = z
  .object({
    NODE_ENV: z.enum(['development', 'test', 'production']).default('development'),
    LOG_LEVEL: z.enum(['fatal', 'error', 'warn', 'info', 'debug', 'trace']).default('info'),
    SERVICE_NAME: z.string().default('communication-service'),

    COMMUNICATION_HTTP_HOST: z.string().default('0.0.0.0'),
    COMMUNICATION_HTTP_PORT: z.coerce.number().int().positive().default(5350),

    COMMUNICATION_DB_CONNECTION: z.string().min(1),
    COMMUNICATION_REDIS_URI: z.string().url(),
    COMMUNICATION_SESSION_DENYLIST_PREFIX: z.string().default('auth:session-denylist'),
    // Fail-closed por defecto: si Redis no responde al chequear el denylist,
    // rechazamos la conexion en vez de asumir "no revocado". Flag de emergencia
    // para operar en fail-open temporalmente si un incidente de Redis tumba
    // todas las conexiones nuevas.
    COMMUNICATION_SESSION_DENYLIST_FAIL_CLOSED: z.coerce.boolean().default(true),

    COMMUNICATION_RABBITMQ_URI: z.string().min(1),
    COMMUNICATION_RABBITMQ_EXCHANGE: z.string().default('taxvision-events'),
    COMMUNICATION_RABBITMQ_QUEUE: z.string().default('communication-events'),
    COMMUNICATION_RABBITMQ_DLQ: z.string().default('communication-events.dlq'),

    COMMUNICATION_JWT_ISSUER: z.string().default('TaxVision.Auth'),
    COMMUNICATION_JWT_AUDIENCE: z.string().default('TaxVision.Services'),
    COMMUNICATION_JWKS_URI: z.string().url(),
    COMMUNICATION_JWKS_CACHE_MAX_AGE_SECONDS: z.coerce.number().int().positive().default(300),

    COMMUNICATION_TURN_URL: z.string().optional(),
    COMMUNICATION_TURN_STATIC_AUTH_SECRET: z.string().optional(),
    COMMUNICATION_TURN_TTL_SECONDS: z.coerce.number().int().positive().default(300),

    // SFU (mediasoup) — meetings con mas de 4 participantes. `listenIp` es la
    // interfaz local donde escucha cada WebRtcTransport; `announcedIp` es la
    // IP publica que se anuncia en los ICE candidates cuando el server esta
    // detras de NAT (cloud VM tipico). Sin `announcedIp`, `listenIp` debe ser
    // ya la IP publica (deploy con IP directa, sin NAT).
    COMMUNICATION_MEDIASOUP_LISTEN_IP: z.string().default('0.0.0.0'),
    COMMUNICATION_MEDIASOUP_ANNOUNCED_IP: z.string().optional(),
    COMMUNICATION_MEDIASOUP_RTC_MIN_PORT: z.coerce.number().int().positive().default(40000),
    COMMUNICATION_MEDIASOUP_RTC_MAX_PORT: z.coerce.number().int().positive().default(49999),
    // 0 = auto (un worker por core disponible, tope 4 en dev para no saturar).
    COMMUNICATION_MEDIASOUP_NUM_WORKERS: z.coerce.number().int().min(0).default(0),

    OTEL_EXPORTER_OTLP_ENDPOINT: z.string().url().optional(),
    OTEL_EXPORTER_OTLP_PROTOCOL: z.enum(['grpc', 'http/protobuf']).default('http/protobuf'),
    OTEL_SERVICE_NAME: z.string().optional(),

    COMMUNICATION_CORS_ORIGINS: z.string().default(''),

    COMMUNICATION_RATE_LIMIT_MESSAGES_PER_MIN: z.coerce.number().int().positive().default(60),
    COMMUNICATION_RATE_LIMIT_TYPING_PER_MIN: z.coerce.number().int().positive().default(120),

    COMMUNICATION_PLATFORM_TENANT_ID: z
      .string()
      .uuid()
      .default('8f58a521-4c25-4d91-9f4e-7ad5df14c001'),
  })
  .parse(process.env);

const corsOrigins = rawEnv.COMMUNICATION_CORS_ORIGINS.split(',')
  .map((s) => s.trim())
  .filter(Boolean);

export const config = {
  env: rawEnv.NODE_ENV,
  isProduction: rawEnv.NODE_ENV === 'production',
  isDevelopment: rawEnv.NODE_ENV === 'development',
  isTest: rawEnv.NODE_ENV === 'test',
  serviceName: rawEnv.SERVICE_NAME,
  logLevel: rawEnv.LOG_LEVEL,

  http: {
    host: rawEnv.COMMUNICATION_HTTP_HOST,
    port: rawEnv.COMMUNICATION_HTTP_PORT,
  },

  database: {
    url: rawEnv.COMMUNICATION_DB_CONNECTION,
  },

  redis: {
    uri: rawEnv.COMMUNICATION_REDIS_URI,
    sessionDenylistPrefix: rawEnv.COMMUNICATION_SESSION_DENYLIST_PREFIX,
    sessionDenylistFailClosed: rawEnv.COMMUNICATION_SESSION_DENYLIST_FAIL_CLOSED,
  },

  rabbitmq: {
    uri: rawEnv.COMMUNICATION_RABBITMQ_URI,
    exchange: rawEnv.COMMUNICATION_RABBITMQ_EXCHANGE,
    queue: rawEnv.COMMUNICATION_RABBITMQ_QUEUE,
    dlq: rawEnv.COMMUNICATION_RABBITMQ_DLQ,
  },

  jwt: {
    issuer: rawEnv.COMMUNICATION_JWT_ISSUER,
    audience: rawEnv.COMMUNICATION_JWT_AUDIENCE,
    jwksUri: rawEnv.COMMUNICATION_JWKS_URI,
    jwksCacheMaxAgeSeconds: rawEnv.COMMUNICATION_JWKS_CACHE_MAX_AGE_SECONDS,
  },

  turn: {
    url: rawEnv.COMMUNICATION_TURN_URL,
    staticAuthSecret: rawEnv.COMMUNICATION_TURN_STATIC_AUTH_SECRET,
    ttlSeconds: rawEnv.COMMUNICATION_TURN_TTL_SECONDS,
  },

  mediasoup: {
    listenIp: rawEnv.COMMUNICATION_MEDIASOUP_LISTEN_IP,
    announcedIp: rawEnv.COMMUNICATION_MEDIASOUP_ANNOUNCED_IP,
    rtcMinPort: rawEnv.COMMUNICATION_MEDIASOUP_RTC_MIN_PORT,
    rtcMaxPort: rawEnv.COMMUNICATION_MEDIASOUP_RTC_MAX_PORT,
    numWorkers: rawEnv.COMMUNICATION_MEDIASOUP_NUM_WORKERS,
  },

  otel: {
    endpoint: rawEnv.OTEL_EXPORTER_OTLP_ENDPOINT,
    protocol: rawEnv.OTEL_EXPORTER_OTLP_PROTOCOL,
    serviceName: rawEnv.OTEL_SERVICE_NAME ?? rawEnv.SERVICE_NAME,
  },

  cors: {
    origins: corsOrigins,
  },

  rateLimit: {
    messagesPerMinute: rawEnv.COMMUNICATION_RATE_LIMIT_MESSAGES_PER_MIN,
    typingPerMinute: rawEnv.COMMUNICATION_RATE_LIMIT_TYPING_PER_MIN,
  },

  platformTenantId: rawEnv.COMMUNICATION_PLATFORM_TENANT_ID.toLowerCase(),
} as const;

export type AppConfig = typeof config;
