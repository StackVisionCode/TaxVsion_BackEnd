import { pino } from 'pino';
import { config } from './config.js';
const options = {
    level: config.logLevel,
    base: { service: config.serviceName, env: config.env },
    timestamp: pino.stdTimeFunctions.isoTime,
    redact: {
        paths: ['*.token', '*.accessToken', '*.clientSecret', 'headers.authorization'],
        censor: '[REDACTED]',
    },
    formatters: { level: (label) => ({ level: label }) },
};
export const logger = pino(options);
//# sourceMappingURL=logger.js.map