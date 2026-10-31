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
  readonly invitationId: string;
  readonly meetingId: string;
  readonly tenantId: string;
  readonly displayName: string;
  /** F6: vida del ticket = vida de la sesion del guest (al vencer, el socket lo saca con `Expired`). */
  readonly ttlSeconds?: number;
}

export async function signJoinTicket(claims: JoinTicketClaims): Promise<string> {
  return new SignJWT({
    tenant_id: claims.tenantId,
    meeting_id: claims.meetingId,
    invitation_id: claims.invitationId,
    display_name: claims.displayName,
    actor_type: 'Guest',
  })
    .setProtectedHeader({ alg: 'HS256', typ: 'JWT' })
    .setSubject(`invitation:${claims.invitationId}`)
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
  if (!tenantId || !meetingId || !invitationId) {
    throw new InvalidJoinTicketError('Guest join ticket is missing required claims.');
  }

  return {
    // F6: el userId del guest ES el invitationId (UUID): las columnas UserId/ActorUserId son uniqueidentifier
    // y un prefijo de texto rompia el upsert del participante y el MeetingEventLog. `actorType: 'Guest'` lo distingue.
    userId: invitationId,
    tenantId,
    actorType: 'Guest',
    permissions: [],
    permissionVersion: 1,
    sessionId: undefined,
    jti: undefined,
    raw: payload,
  };
}
