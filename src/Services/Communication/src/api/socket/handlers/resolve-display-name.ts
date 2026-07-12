import type { UserDirectoryRepository } from '../../../application/ports/user-directory-repository.js';

/**
 * Resuelve el displayName real de un usuario via `UserDirectoryEntry`
 * (hidratado por los consumers de auth.user.registered/profile_updated).
 * Si el usuario aun no esta en el directorio (race entre el evento de Auth y
 * la primera conexion de socket, o el usuario nunca disparo esos eventos),
 * cae de vuelta al userId — mismo comportamiento que antes de esta proyeccion,
 * nunca peor.
 */
export async function resolveDisplayName(
  userDirectory: UserDirectoryRepository,
  userId: string,
): Promise<string> {
  const entry = await userDirectory.findByUserId(userId);
  return entry?.displayName ?? userId;
}
