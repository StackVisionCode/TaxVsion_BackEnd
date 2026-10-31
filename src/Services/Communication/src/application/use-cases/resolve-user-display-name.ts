import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';

/**
 * Nombre legible de un usuario para eventos y emails (F6): directorio de usuarios si esta, si no el
 * `fallback` (nombre del participante, email) y nunca un GUID. Best-effort: un fallo del directorio
 * no tumba el caso de uso.
 */
export async function resolveUserDisplayName(
  userDirectory: Pick<UserDirectoryRepository, 'findByUserId'> | undefined,
  userId: string,
  fallback?: string | null,
): Promise<string> {
  if (userDirectory) {
    try {
      const entry = await userDirectory.findByUserId(userId);
      if (entry?.displayName?.trim()) return entry.displayName.trim();
    } catch {
      // Sin directorio no se bloquea nada: cae al fallback.
    }
  }
  const safe = fallback?.trim();
  return safe && safe.length > 0 ? safe : 'Host';
}
