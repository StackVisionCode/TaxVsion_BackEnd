/**
 * Perfil visible de una persona en vivo (F5 de avatares). Se emite cuando Communication aplica
 * `auth.user.profile_updated.v1` (staff: nombre o foto) o cuando cambia la foto de un cliente que tiene
 * cuenta de portal (`customer.updated.v1` / `customer.archived.v1`, junto al `customer.changed` de siempre).
 *
 * Destinatarios:
 *   - `t:{tenantId}:staff`: el CRM actualiza sus directorios, participantes y el navbar;
 *   - `t:{tenantId}:u:{userId}`: las otras pestañas del propio usuario (incluido el portal).
 * NO va a `t:{tenantId}` ni a `:members`: el portal oculta a proposito la identidad del staff fuera de
 * sus conversaciones, asi que un cliente no recibe los cambios de perfil de otras personas. Ve la foto
 * nueva de su preparador en la siguiente lectura de participantes.
 *
 * El front arma la URL de la imagen: `/storage/avatars/{avatarFileId}`; null = sin foto (iniciales).
 */

// ---------- Server -> Client ----------

export interface UserProfileChangedDto {
  /** UserId de Auth (staff o cuenta de portal). */
  userId: string;
  displayName: string;
  avatarFileId: string | null;
}

export const UserProfileSocketEvents = {
  /** Cambió el nombre o la foto de un usuario; el front parchea sus stores por `userId`. */
  Changed: 'user.profile.changed',
} as const;
