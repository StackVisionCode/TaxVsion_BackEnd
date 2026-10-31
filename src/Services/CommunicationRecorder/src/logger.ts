import { pino, type Logger } from 'pino';

export function createLogger(level: string, serviceName: string, env: string): Logger {
  return pino({
    level,
    base: { service: serviceName, env },
    timestamp: pino.stdTimeFunctions.isoTime,
    redact: {
      paths: ['*.token', '*.accessToken', '*.secretKey', 'headers.authorization', 'req.headers.authorization'],
      censor: '[REDACTED]',
    },
    formatters: { level: (label) => ({ level: label }) },
  });
}
