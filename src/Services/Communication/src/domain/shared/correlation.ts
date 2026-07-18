import { nanoid } from 'nanoid';

/**
 * CorrelationId — mismo espiritu que el .NET CorrelationContext. Lo tomamos del
 * header X-Correlation-Id si viene, y si no generamos uno estable. Todos los
 * logs, spans y integration events publicados incluyen este id.
 */
export const CORRELATION_HEADER = 'x-correlation-id';

export function generateCorrelationId(): string {
  return nanoid(24);
}

const CORRELATION_REGEX = /^[A-Za-z0-9._-]{1,128}$/;

export function normalizeCorrelationId(input: string | undefined | null): string {
  if (!input || !CORRELATION_REGEX.test(input)) return generateCorrelationId();
  return input;
}
