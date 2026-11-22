import { pino, type Logger, type LoggerOptions } from 'pino';
import { config } from '../config.js';

/**
 * Logger estructurado JSON (Pino). Sin colores en produccion — se envia a Loki
 * via OTLP directamente. En desarrollo agregamos pino-pretty via CLI cuando se
 * quiere leer humanamente (nunca en produccion).
 *
 * Cierra CRIT-1 del legacy: nada de console.log con emojis.
 */
interface LoggedRequest {
  method?: string;
  url?: string;
  headers?: Record<string, unknown>;
  host?: string;
  ip?: string;
  socket?: { remotePort?: number };
}

/** Oculta el texto de búsqueda (`q`) de una URL: puede ser el nombre de un cliente. */
export function maskSearchText(url: string): string {
  return url.replace(/([?&]q=)[^&#]*/g, '$1[REDACTED]');
}

const options: LoggerOptions = {
  level: config.logLevel,
  base: {
    service: config.serviceName,
    env: config.env,
  },
  timestamp: pino.stdTimeFunctions.isoTime,
  redact: {
    // Nunca loguear campos sensibles. Redact deep-path style.
    paths: [
      'req.headers.authorization',
      'headers.authorization',
      '*.token',
      '*.accessToken',
      '*.refreshToken',
      '*.password',
      '*.pin',
      '*.otp',
    ],
    censor: '[REDACTED]',
  },
  formatters: {
    level: (label) => ({ level: label }),
  },
  // Mismos campos que el serializador de Fastify, con la URL sin el texto de búsqueda.
  serializers: {
    req: (req: LoggedRequest) => ({
      method: req.method,
      url: typeof req.url === 'string' ? maskSearchText(req.url) : req.url,
      version: req.headers?.['accept-version'],
      host: req.host,
      remoteAddress: req.ip,
      remotePort: req.socket?.remotePort,
    }),
  },
};

export const logger: Logger = pino(options);
