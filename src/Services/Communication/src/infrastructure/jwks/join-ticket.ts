import { SignJWT, jwtVerify } from 'jose';
import { config } from '../config.js';
import type { AuthenticatedPrincipal } from './jwt-verifier.js';

/**
 * Fase Backend 5 — shortLivedJoinTicket para guests sin cuenta Auth. HS256
 * con un secreto LOCAL de Communication (nunca sale de este proceso) — no
 * confundir con el JWKS RS256 remoto de Auth usado en jwt-verifier.ts: ese
 * ticket se emite Y se verifica en el mismo servicio, no hay nada que
 * publicar/rotar cross-service.
 *
 * `sub` = `invitation:{invitationId}` (nunca un userId real). El resto de
 * claims son las unicas que build-io.ts necesita para sintetizar un
 * AuthenticatedPrincipal "Guest" sin volver a tocar la base de datos.
 */
const secretKey = new TextEncoder().encode(config.meetingInvitations.joinTicketSecret);

export interface JoinTicketClaims {
  /** Invitacion personal; ausente cuando entra por el enlace de la reunion (D-68). */
  readonly invitationId?: string;
  /** D-68: por enlace, cada persona tiene su propio id (UUID); por invitacion es el id de la invitacion. */
  readonly guestUserId?: string;
  /** D-68: version del enlace con la que se emitio (si se rota o apaga, ya no entra uno nuevo). */
  readonly guestLinkVersion?: number;
  readonly meetingId: string;
  readonly tenantId: string;
  readonly displayName: string;
  /** F6: vida del ticket = vida de la sesion del guest (al vencer, el socket lo saca con `Expired`). */
  readonly ttlSeconds?: number;
}

export async function signJoinTicket(claims: JoinTicketClaims): Promise<string> {
  const guestUserId = claims.guestUserId ?? claims.invitationId;
  if (!guestUserId) throw new Error('A guest join ticket needs an invitation or a guest id.');
  return new SignJWT({
    tenant_id: claims.tenantId,
    meeting_id: claims.meetingId,
    guest_id: guestUserId,
    ...(claims.invitationId ? { invitation_id: claims.invitationId } : {}),
    ...(claims.guestLinkVersion !== undefined ? { link_version: claims.guestLinkVersion } : {}),
    display_name: claims.displayName,
    actor_type: 'Guest',
  })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setSubject(claims.invitationId ? `invitation:${claims.invitationId}` : `guest:${guestUserId}`)
    .setIssuedAt()
    .setExpirationTime(`${Math.max(claims.ttlSeconds ?? 0, config.meetingInvitations.joinTicketTtlSeconds)}s`)
    .sign(secretKey);
}

export class InvalidJoinTicketError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'InvalidJoinTicketError';
  }
}

/**
 * Sintetiza un AuthenticatedPrincipal "Guest" con permissions=[] — cualquier
 * `hasPermission(...)` sobre este principal falla salvo el bypass explicito
 * que meeting-handlers.ts hace para el evento Join. Esto es lo que impide
 * que un guest arranque grabaciones, sea cohost o promueva a otros sin tener
 * que duplicar chequeos en cada handler.
 */
export async function verifyGuestJoinTicket(ticket: string): Promise<AuthenticatedPrincipal> {
  const { payload } = await jwtVerify(ticket, secretKey, { algorithms: ['HS256'] }).catch(() => {
    throw new InvalidJoinTicketError('Guest join ticket is invalid or expired.');
  });

  const tenantId = typeof payload['tenant_id'] === 'string' ? payload['tenant_id'] : undefined;
  const meetingId = typeof payload['meeting_id'] === 'string' ? payload['meeting_id'] : undefined;
  const invitationId = typeof payload['invitation_id'] === 'string' ? payload['invitation_id'] : undefined;
  const linkVersion = typeof payload['link_version'] === 'number' ? payload['link_version'] : undefined;
  // Tickets anteriores a D-68 no traen guest_id: el guest era el id de la invitacion.
  const guestUserId = typeof payload['guest_id'] === 'string' ? payload['guest_id'] : invitationId;
  if (!tenantId || !meetingId || !guestUserId || (!invitationId && linkVersion === undefined)) {
    throw new InvalidJoinTicketError('Guest join ticket is missing required claims.');
  }

  return {
    // F6: el userId del guest es un UUID (id de la invitacion o, por enlace, uno propio): las columnas
    // UserId/ActorUserId son uniqueidentifier. `actorType: 'Guest'` lo distingue.
    userId: guestUserId,
    tenantId,
    actorType: 'Guest',
    permissions: [],
    permissionVersion: 1,
    sessionId: undefined,
    jti: undefined,
    raw: payload,
  };
}
