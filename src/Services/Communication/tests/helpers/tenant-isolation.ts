import { expect } from 'vitest';
import type { Result } from '../../src/domain/shared/result.js';

/**
 * Fase Backend 11 — assertion reutilizable para tests de aislamiento tenant:
 * un use case invocado con el `tenantId` de un tenant que NO es el dueño real
 * del recurso debe fallar (tipicamente NotFound/NotParticipant — nunca debe
 * devolver el recurso ajeno ni tener efecto alguno sobre el). El patron en si
 * (repos fake tenant-scoped + invocar el use case con un tenantId "foreign")
 * vive en cada archivo de test porque el shape del command/deps difiere por
 * use case; esta funcion factoriza solo la aserción final, para no repetir
 * `expect(result.isSuccess).toBe(false)` + el mensaje de fallo en cada sitio.
 *
 * Nota de alcance honesta: cubre el nivel use-case (con fakes en memoria que
 * SI filtran por tenant, igual que los repos Prisma reales) — no es un test
 * HTTP/E2E de extremo a extremo. Aplicado hoy a una muestra representativa
 * (chat: send-message, mark-messages-read); extenderlo al resto de la
 * superficie (calls, meetings, recordings) es trabajo de seguimiento natural,
 * no algo que esta fase reclame haber cubierto en su totalidad.
 */
export function expectRejectedCrossTenant<T>(
  result: Result<T>,
  allowedErrorCodes?: readonly string[],
): void {
  expect(result.isSuccess, 'cross-tenant access must be rejected, never return the foreign resource').toBe(false);
  if (!result.isSuccess && allowedErrorCodes) {
    expect(allowedErrorCodes).toContain(result.error.code);
  }
}
