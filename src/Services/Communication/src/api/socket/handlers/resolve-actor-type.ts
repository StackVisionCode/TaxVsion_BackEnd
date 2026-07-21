import type { UserDirectoryRepository } from '../../../application/ports/user-directory-repository.js';

/**
 * Fase B3 — resuelve el actorType real de un usuario via `UserDirectoryEntry`
 * (hidratado por auth-consumers.ts para CUALQUIER actor con email: empleados,
 * admins Y customer-portal por igual — ver bindAuthConsumers). Reemplaza los
 * 4 sitios que hardcodeaban `actorType: 'TenantEmployee'` para el destinatario
 * sin importar quien fuera en realidad (chat-handlers.ts x3,
 * meeting-host-actions.ts x1).
 *
 * Si el usuario aun no esta en el directorio (misma race que
 * resolveDisplayName), cae a 'TenantEmployee' — mismo default que ya usa
 * auth-consumers.ts cuando Auth no manda actorType en el evento.
 */
export async function resolveActorType(userDirectory: UserDirectoryRepository, userId: string): Promise<string> {
  const entry = await userDirectory.findByUserId(userId);
  return entry?.actorType ?? 'TenantEmployee';
}
