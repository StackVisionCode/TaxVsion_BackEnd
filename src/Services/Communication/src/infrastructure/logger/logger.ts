import { pino, type Logger, type LoggerOptions } from 'pino';
import { config } from '../config.js';

/**
 * Logger estructurado JSON (Pino). Sin colores en produccion — se envia a Loki
 * via OTLP directamente. En desarrollo agregamos pino-pretty via CLI cuando se
 * quiere leer humanamente (nunca en produccion).
 *
 * Cierra CRIT-1 del legacy: nada de console.log con emojis.
 */
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
};

export const logger: Logger = pino(options);
