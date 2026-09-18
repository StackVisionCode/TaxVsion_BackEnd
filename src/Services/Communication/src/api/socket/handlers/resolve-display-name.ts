import type { UserDirectoryRepository } from '../../../application/ports/user-directory-repository.js';

/**
 * Resuelve el displayName real de un usuario via `UserDirectoryEntry`
 * (hidratado por los consumers de auth.user.registered/profile_updated).
 * Si el usuario aun no esta en el directorio (race entre el evento de Auth y
 * la primera conexion de socket, un owner viejo de onboarding sin fila, o el
 * usuario nunca disparo esos eventos), cae al `fallback` legible (p.ej. el email
 * del JWT) antes que al userId crudo — así una tile de meeting NUNCA muestra un
 * GUID. Solo cae al userId si tampoco hay fallback.
 */
export async function resolveDisplayName(
  userDirectory: UserDirectoryRepository,
  userId: string,
  fallback?: string | null,
): Promise<string> {
  const entry = await userDirectory.findByUserId(userId);
  return entry?.displayName ?? (fallback && fallback.trim() ? fallback.trim() : userId);
}
