import { createHmac, timingSafeEqual } from 'node:crypto';
import { config } from '../config.js';

/**
 * D-68: enlace de la reunion para externos. El token no se guarda: se deriva con HMAC del secreto local
 * de los tickets de invitado (con su propia etiqueta) a partir del id de la reunion y la version del
 * enlace. Rotar o volver a encender el enlace sube la version y los enlaces anteriores dejan de validar.
 */
const LABEL = 'meeting-guest-link';
const MAC_LENGTH = 32;
/** Token de invitacion personal: 32 bytes en hex. */
const INVITATION_TOKEN_LENGTH = 64;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const secret = (): string => config.meetingInvitations.joinTicketSecret;

function macOf(meetingId: string, version: number): string {
  return createHmac('sha256', secret()).update(`${LABEL}:${meetingId.toLowerCase()}:${version}`).digest('base64url').slice(0, MAC_LENGTH);
}

export function signGuestLinkToken(meetingId: string, version: number): string {
  return `${meetingId.toLowerCase()}.${macOf(meetingId, version)}`;
}

/** Forma de enlace de reunion (`{meetingId}.{mac}`); los de invitacion son hex sin punto. */
export function parseGuestLinkToken(token: string): { meetingId: string; mac: string } | null {
  const [meetingId, mac, extra] = token.split('.');
  if (extra !== undefined || !meetingId || !mac || !UUID.test(meetingId) || mac.length !== MAC_LENGTH) return null;
  return { meetingId: meetingId.toLowerCase(), mac };
}

/** Lo que aceptan las rutas publicas de invitado: token de invitacion (64) o enlace de la reunion. */
export function isGuestAccessToken(token: string): boolean {
  return token.length === INVITATION_TOKEN_LENGTH || parseGuestLinkToken(token) !== null;
}

export function verifyGuestLinkToken(token: string, version: number): boolean {
  const parsed = parseGuestLinkToken(token);
  if (!parsed) return false;
  const expected = Buffer.from(macOf(parsed.meetingId, version));
  const actual = Buffer.from(parsed.mac);
  return expected.length === actual.length && timingSafeEqual(expected, actual);
}
