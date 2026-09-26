import type { FastifyReply } from 'fastify';

/**
 * Contrato único del 429, espejo de `RateLimitRejection` del lado .NET: header `Retry-After` en
 * segundos y body `{ code: "RateLimit.Exceeded", message, retryAfterSeconds, policy }`. El front usa
 * `retryAfterSeconds` para decirle al usuario cuánto esperar.
 */
export const RATE_LIMIT_CODE = 'RateLimit.Exceeded';

export function rateLimitMessage(retryAfterSeconds: number): string {
  const seconds = normalizeSeconds(retryAfterSeconds);
  return `You're making requests too quickly. Please try again in ${seconds === 1 ? '1 second' : `${seconds} seconds`}.`;
}

/** Responde el 429. Devuelve el `reply` para que un hook async lo retorne y Fastify corte la cadena. */
export function sendRateLimited(reply: FastifyReply, retryAfterSeconds: number, policy: string): FastifyReply {
  const seconds = normalizeSeconds(retryAfterSeconds);
  return reply
    .code(429)
    .header('Retry-After', String(seconds))
    .send({ code: RATE_LIMIT_CODE, message: rateLimitMessage(seconds), retryAfterSeconds: seconds, policy });
}

function normalizeSeconds(seconds: number): number {
  return Math.max(1, Math.ceil(seconds));
}
