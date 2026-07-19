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
    //
    // z.coerce.boolean() NO parsea "true"/"false" como texto — hace Boolean(valor),
    // y CUALQUIER string no vacio (incluido literalmente "false") da true. Con este
    // flag, poner COMMUNICATION_SESSION_DENYLIST_FAIL_CLOSED=false en .env para
    // probar el modo fail-open NO tendria efecto (silenciosamente seguiria en
    // fail-closed) — no esta activo hoy porque la var no esta seteada en ningun
    // .env, pero ver mismo bug ya confirmado en CommunicationTranscriptWorker/src/config.ts
    // (TRANSCRIPT_WORKER_MINIO_USE_SSL, causaba un EPROTO real en runtime).
    COMMUNICATION_SESSION_DENYLIST_FAIL_CLOSED: z
      .string()
      .default('true')
      .transform((value) => value === 'true'),

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

    // Fase Backend 11 — antes literales inline en cada socket handler; ahora
    // configurables por env sin tocar codigo. Defaults = los valores que ya
    // estaban hardcodeados (sin cambio de comportamiento fuera de .env).
    COMMUNICATION_RATE_LIMIT_CALL_INITIATE_MAX: z.coerce.number().int().positive().default(10),
    COMMUNICATION_RATE_LIMIT_CALL_INITIATE_WINDOW_SECONDS: z.coerce.number().int().positive().default(30),
    COMMUNICATION_RATE_LIMIT_CALL_SIGNAL_MAX: z.coerce.number().int().positive().default(60),
    COMMUNICATION_RATE_LIMIT_CALL_SIGNAL_WINDOW_SECONDS: z.coerce.number().int().positive().default(10),
    COMMUNICATION_RATE_LIMIT_MEETING_CHAT_SEND_MAX: z.coerce.number().int().positive().default(30),
    COMMUNICATION_RATE_LIMIT_MEETING_CHAT_SEND_WINDOW_SECONDS: z.coerce.number().int().positive().default(10),
    COMMUNICATION_RATE_LIMIT_CHAT_SEND_MAX: z.coerce.number().int().positive().default(30),
    COMMUNICATION_RATE_LIMIT_CHAT_SEND_WINDOW_SECONDS: z.coerce.number().int().positive().default(10),
    COMMUNICATION_RATE_LIMIT_CHAT_EDIT_MAX: z.coerce.number().int().positive().default(20),
    COMMUNICATION_RATE_LIMIT_CHAT_EDIT_WINDOW_SECONDS: z.coerce.number().int().positive().default(10),
    COMMUNICATION_RATE_LIMIT_CHAT_TYPING_MAX: z.coerce.number().int().positive().default(20),
    COMMUNICATION_RATE_LIMIT_CHAT_TYPING_WINDOW_SECONDS: z.coerce.number().int().positive().default(10),
    // F11 QA gap — build-server.ts (limite HTTP global) y meeting-invitations.route.ts
    // (join-by-token/by-code, publicos) tenian estos numeros literales inline pese a que
    // el docblock de la ruta ya afirmaba que salian de config.rateLimit. Mismos defaults
    // que los literales que reemplazan, sin cambio de comportamiento fuera de .env.
    COMMUNICATION_RATE_LIMIT_HTTP_GLOBAL_MAX: z.coerce.number().int().positive().default(300),
    COMMUNICATION_RATE_LIMIT_HTTP_GLOBAL_WINDOW_SECONDS: z.coerce.number().int().positive().default(60),
    COMMUNICATION_RATE_LIMIT_MEETING_JOIN_TOKEN_MAX: z.coerce.number().int().positive().default(5),
    COMMUNICATION_RATE_LIMIT_MEETING_JOIN_TOKEN_WINDOW_SECONDS: z.coerce.number().int().positive().default(60),
    COMMUNICATION_RATE_LIMIT_MEETING_JOIN_CODE_MAX: z.coerce.number().int().positive().default(20),
    COMMUNICATION_RATE_LIMIT_MEETING_JOIN_CODE_WINDOW_SECONDS: z.coerce.number().int().positive().default(60),

    COMMUNICATION_PLATFORM_TENANT_ID: z
      .string()
      .uuid()
      .default('8f58a521-4c25-4d91-9f4e-7ad5df14c001'),

    // Fase Backend 5 — invitaciones a meetings. Secreto local HS256, propio de
    // Communication, para el shortLivedJoinTicket del guest (nada que ver con
    // el JWKS RS256 de Auth usado para usuarios con cuenta).
    COMMUNICATION_FRONTEND_BASE_URL: z.string().url().default('http://localhost:5173'),
    COMMUNICATION_JOIN_TICKET_SECRET: z.string().min(32),
    COMMUNICATION_JOIN_TICKET_TTL_SECONDS: z.coerce.number().int().positive().default(300),

    // Fase Backend 8 — M2M sync HTTP a CloudStorage para validar metadata de
    // grabaciones al attach (bug #245: rechazar files size=0). Reusa el mismo
    // client-id/secret que ya existia en .env para el TranscriptWorker; no hay
    // separacion 1:1 servicio→credencial en este backend, misma politica que
    // Signature/Notification (ver memoria feedback_m2m_config_key_binding).
    COMMUNICATION_SERVICE_AUTH_CLIENT_ID: z.string().default('communication-worker'),
    COMMUNICATION_SERVICE_AUTH_CLIENT_SECRET: z.string().min(1),
    COMMUNICATION_AUTH_BASE_URL: z.string().url().default('http://localhost:5124'),
    COMMUNICATION_CLOUDSTORAGE_BASE_URL: z.string().url().default('http://localhost:5330'),
  })
  .parse(process.env);

const corsOrigins = rawEnv.COMMUNICATION_CORS_ORIGINS.split(',')
  .map((s) => s.trim())
  .filter(Boolean);

// Fase Backend 11 — fail-closed en produccion: un default vacio silencioso
// terminaria permitiendo cualquier origen (ver build-server.ts, `origin: true`
// cuando `cors.origins` esta vacio) — inaceptable fuera de dev/test. En
// development/test se mantiene permisivo (vacio = allow-all) por conveniencia
// local, igual que siempre.
if (rawEnv.NODE_ENV === 'production' && corsOrigins.length === 0) {
  throw new Error(
    'COMMUNICATION_CORS_ORIGINS is required and must be non-empty when NODE_ENV=production (fail-closed CORS policy).',
  );
}

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
    // COMMUNICATION_TURN_URL admite una o varias URLs separadas por coma
    // (ej. "turn:host:3478,turns:host:5349?transport=tcp") — se ofrecen todas
    // como ICE servers distintos con las mismas credenciales HMAC, asi el
    // navegador puede caer a turns: (parece HTTPS, puerto 5349) en redes que
    // bloquean UDP generico pero dejan pasar TLS saliente.
    urls: rawEnv.COMMUNICATION_TURN_URL
      ? rawEnv.COMMUNICATION_TURN_URL.split(',')
          .map((u) => u.trim())
          .filter(Boolean)
      : [],
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
    callInitiate: {
      maxPerWindow: rawEnv.COMMUNICATION_RATE_LIMIT_CALL_INITIATE_MAX,
      windowSeconds: rawEnv.COMMUNICATION_RATE_LIMIT_CALL_INITIATE_WINDOW_SECONDS,
    },
    callSignal: {
      maxPerWindow: rawEnv.COMMUNICATION_RATE_LIMIT_CALL_SIGNAL_MAX,
      windowSeconds: rawEnv.COMMUNICATION_RATE_LIMIT_CALL_SIGNAL_WINDOW_SECONDS,
    },
    meetingChatSend: {
      maxPerWindow: rawEnv.COMMUNICATION_RATE_LIMIT_MEETING_CHAT_SEND_MAX,
      windowSeconds: rawEnv.COMMUNICATION_RATE_LIMIT_MEETING_CHAT_SEND_WINDOW_SECONDS,
    },
    chatSend: {
      maxPerWindow: rawEnv.COMMUNICATION_RATE_LIMIT_CHAT_SEND_MAX,
      windowSeconds: rawEnv.COMMUNICATION_RATE_LIMIT_CHAT_SEND_WINDOW_SECONDS,
    },
    chatEdit: {
      maxPerWindow: rawEnv.COMMUNICATION_RATE_LIMIT_CHAT_EDIT_MAX,
      windowSeconds: rawEnv.COMMUNICATION_RATE_LIMIT_CHAT_EDIT_WINDOW_SECONDS,
    },
    chatTyping: {
      maxPerWindow: rawEnv.COMMUNICATION_RATE_LIMIT_CHAT_TYPING_MAX,
      windowSeconds: rawEnv.COMMUNICATION_RATE_LIMIT_CHAT_TYPING_WINDOW_SECONDS,
    },
    httpGlobal: {
      maxPerWindow: rawEnv.COMMUNICATION_RATE_LIMIT_HTTP_GLOBAL_MAX,
      windowSeconds: rawEnv.COMMUNICATION_RATE_LIMIT_HTTP_GLOBAL_WINDOW_SECONDS,
    },
    meetingJoinByToken: {
      maxPerWindow: rawEnv.COMMUNICATION_RATE_LIMIT_MEETING_JOIN_TOKEN_MAX,
      windowSeconds: rawEnv.COMMUNICATION_RATE_LIMIT_MEETING_JOIN_TOKEN_WINDOW_SECONDS,
    },
    meetingJoinByCode: {
      maxPerWindow: rawEnv.COMMUNICATION_RATE_LIMIT_MEETING_JOIN_CODE_MAX,
      windowSeconds: rawEnv.COMMUNICATION_RATE_LIMIT_MEETING_JOIN_CODE_WINDOW_SECONDS,
    },
  },

  platformTenantId: rawEnv.COMMUNICATION_PLATFORM_TENANT_ID.toLowerCase(),

  meetingInvitations: {
    frontendBaseUrl: rawEnv.COMMUNICATION_FRONTEND_BASE_URL,
    joinTicketSecret: rawEnv.COMMUNICATION_JOIN_TICKET_SECRET,
    joinTicketTtlSeconds: rawEnv.COMMUNICATION_JOIN_TICKET_TTL_SECONDS,
  },

  serviceAuth: {
    clientId: rawEnv.COMMUNICATION_SERVICE_AUTH_CLIENT_ID,
    clientSecret: rawEnv.COMMUNICATION_SERVICE_AUTH_CLIENT_SECRET,
    authBaseUrl: rawEnv.COMMUNICATION_AUTH_BASE_URL,
  },
  cloudStorage: {
    baseUrl: rawEnv.COMMUNICATION_CLOUDSTORAGE_BASE_URL,
  },
} as const;

export type AppConfig = typeof config;
