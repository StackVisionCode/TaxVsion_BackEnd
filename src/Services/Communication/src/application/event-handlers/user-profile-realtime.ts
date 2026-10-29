import { randomUUID } from 'node:crypto';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  UserProfileSocketEvents,
  type UserProfileChangedDto,
} from '../../contracts/socket/user-profile-socket-events.js';

/** `user.profile.changed` al staff del tenant y a las pestañas del propio usuario (ver el contrato). */
export function emitUserProfileChanged(
  emitter: RealtimeEmitter,
  tenantId: string,
  payload: UserProfileChangedDto,
  correlationId?: string,
): void {
  const envelope = {
    eventId: randomUUID(),
    correlationId: correlationId ?? '',
    emittedAtUtc: new Date().toISOString(),
    payload,
  };
  emitter.emitToTenantStaff<UserProfileChangedDto>({
    tenantId,
    event: UserProfileSocketEvents.Changed,
    envelope,
  });
  emitter.emitToUser<UserProfileChangedDto>({
    tenantId,
    userId: payload.userId,
    event: UserProfileSocketEvents.Changed,
    envelope,
  });
}

/**
 * Lee un GUID opcional del payload distinguiendo "no vino" de "vino null": Wolverine serializa los
 * null, asi que la clave ausente solo aparece con publicadores viejos (antes de F2/F3).
 *   - undefined → campo ausente: no tocar lo guardado;
 *   - null → quitado explicitamente.
 */
export function readOptionalGuid(
  payload: Readonly<Record<string, unknown>>,
  ...keys: string[]
): string | null | undefined {
  for (const key of keys) {
    if (!(key in payload)) continue;
    const value = payload[key];
    if (value === null) return null;
    if (typeof value === 'string') {
      const trimmed = value.trim().toLowerCase();
      // Guid.Empty no es una foto.
      return trimmed.length === 0 || trimmed === '00000000-0000-0000-0000-000000000000' ? null : trimmed;
    }
    return undefined;
  }
  return undefined;
}
