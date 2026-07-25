import { expect } from 'vitest';

/**
 * Fase 7.2 (Actor_Type_Authorization_Layers_Plan.md) — assertion reutilizable para tests de
 * aislamiento cross-actor, hermana de `expectRejectedCrossTenant` (mismo criterio: factoriza solo
 * la aserción final, no el armado de fixtures). A diferencia del eje tenant, Communication no tiene
 * un chequeo de actor_type propio en cada handler — `hasPermission()` (domain/shared/permissions.ts)
 * es el único punto de enforcement, y confía en que el arreglo `permissions` del JWT ya viene
 * correcto (lo garantiza `ActorTypeRoleGuard` del lado de Auth, ver Fase 2/7.1). Por eso esta
 * assertion opera directo sobre el boolean que devuelve `hasPermission`, no sobre un `Result<T>`.
 */
export function expectRejectedCrossActorType(hasPermissionResult: boolean): void {
  expect(hasPermissionResult, 'a caller without the required permission must never pass hasPermission()').toBe(
    false,
  );
}
