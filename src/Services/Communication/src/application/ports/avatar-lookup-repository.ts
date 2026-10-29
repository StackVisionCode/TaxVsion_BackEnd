/**
 * Lectura en lote de lo necesario para decidir la foto de N usuarios (F5 de avatares). Una sola
 * consulta por llamada: el adaptador cruza UserDirectoryEntry, CustomerPortalAccount y
 * CustomerDirectoryEntry. La regla (staff vs portal) la aplica `AvatarResolver`, no el adaptador.
 *
 * Sin filtro de tenant a proposito (mismo criterio que `UserDirectoryRepository.findByUserIds`): el
 * UserId es global y los ids llegan de listas ya autorizadas (participantes, remitentes). Soporte cruza
 * tenants (agente de plataforma <-> cliente del tenant).
 */
export interface AvatarLookupRow {
  /** En minusculas. */
  readonly userId: string;
  /** ActorType de UserDirectoryEntry; null si el usuario no tiene fila. */
  readonly actorType: string | null;
  /** Foto del staff (Auth). */
  readonly userAvatarFileId: string | null;
  /** Foto de la ficha del cliente enlazado por su cuenta de portal activa. */
  readonly customerAvatarFileId: string | null;
  readonly hasActivePortalAccount: boolean;
}

export interface AvatarLookupRepository {
  findByUserIds(userIds: readonly string[]): Promise<AvatarLookupRow[]>;
}
