import { logger } from '../../infrastructure/logger/logger.js';
import type { AvatarLookupRepository, AvatarLookupRow } from '../ports/avatar-lookup-repository.js';

/**
 * Foto de perfil a la hora de leer (F5 de avatares). No se copia a las filas de participantes ni de
 * mensajes: se resuelve por lote al armar cada respuesta, asi un cambio de foto se ve en la siguiente
 * lectura sin migrar nada.
 *
 * Regla:
 *   - staff → `UserDirectoryEntry.AvatarFileId` (la foto vive en Auth);
 *   - cliente del portal → `CustomerPortalAccount` (userId → customerId) →
 *     `CustomerDirectoryEntry.AvatarFileId` (la foto vive en la ficha del cliente);
 *   - sin fila (invitado, placeholder "Support Team") → null.
 *
 * Contrato en los DTOs: `avatarFileId` ausente = "no se resolvio aca, conserva lo que tengas";
 * `null` = sin foto. El front arma la URL `/storage/avatars/{id}`.
 */
export interface AvatarLookup {
  of(userId: string): string | null;
}

export interface AvatarResolver {
  /** Una sola consulta para todos los ids (deduplicados). */
  resolve(userIds: readonly string[]): Promise<AvatarLookup>;
}

export const NO_AVATARS: AvatarLookup = { of: () => null };

export function pickAvatarFileId(row: AvatarLookupRow): string | null {
  if (row.hasActivePortalAccount || row.actorType === 'CustomerPortal') {
    return row.customerAvatarFileId;
  }
  return row.userAvatarFileId;
}

/** Tope por consulta: SQL Server admite 2100 parametros. */
const MAX_IDS_PER_QUERY = 500;

export class DirectoryAvatarResolver implements AvatarResolver {
  constructor(private readonly lookup: AvatarLookupRepository) {}

  async resolve(userIds: readonly string[]): Promise<AvatarLookup> {
    const unique = [...new Set(userIds.map((id) => id.trim().toLowerCase()).filter((id) => id.length > 0))];
    if (unique.length === 0) return NO_AVATARS;

    const byUserId = new Map<string, string | null>();
    for (let i = 0; i < unique.length; i += MAX_IDS_PER_QUERY) {
      const rows = await this.lookup.findByUserIds(unique.slice(i, i + MAX_IDS_PER_QUERY));
      for (const row of rows) byUserId.set(row.userId.toLowerCase(), pickAvatarFileId(row));
    }
    return { of: (userId) => byUserId.get(userId.toLowerCase()) ?? null };
  }
}

/**
 * Fail-soft: sin resolver (tests, wiring parcial) o si la consulta falla, la respuesta sale igual,
 * solo sin fotos. Una foto nunca debe tumbar la lista de conversaciones ni un join.
 */
export async function resolveAvatars(
  resolver: AvatarResolver | undefined,
  userIds: readonly string[],
): Promise<AvatarLookup | undefined> {
  if (!resolver) return undefined;
  try {
    return await resolver.resolve(userIds);
  } catch (err) {
    logger.warn({ err: (err as Error).message }, 'avatar lookup failed; responding without avatars');
    return undefined;
  }
}
