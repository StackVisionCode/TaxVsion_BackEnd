import type { UserDirectoryRepository } from '../../../application/ports/user-directory-repository.js';

/**
 * Fase B3 — resuelve el actorType real de un usuario via `UserDirectoryEntry`
 * (hidratado por auth-consumers.ts para CUALQUIER actor con email: empleados,
 * admins Y customer-portal por igual — ver bindAuthConsumers). Reemplaza los
 * 4 sitios que hardcodeaban `actorType: 'TenantEmployee'` para el destinatario
 * sin importar quien fuera en realidad (chat-handlers.ts x3,
 * meeting-host-actions.ts x1).
 *
 * Fail-closed (ActorType Fase 5 del plan de autorizacion): si el usuario aun
 * no esta en el directorio (misma race que resolveDisplayName), esto NO es el
 * caller autenticandose a si mismo — es resolver el actor type de OTRO
 * usuario (el destinatario/miembro), asi que no hay JWT propio que rechazar.
 * "Fail closed" aca significa devolver `null` (datos insuficientes) en vez de
 * asumir 'TenantEmployee' — el caller debe tratar `null` como "no se puede
 * completar la operacion todavia", nunca proceder con una identidad adivinada.
 */
export async function resolveActorType(
  userDirectory: UserDirectoryRepository,
  userId: string,
): Promise<string | null> {
  const entry = await userDirectory.findByUserId(userId);
  return entry?.actorType ?? null;
}
