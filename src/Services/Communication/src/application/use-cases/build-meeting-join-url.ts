import type { MeetingInviteeKind } from '../../domain/meetings/meeting-invitation.js';

export interface BuildMeetingJoinUrlInput {
  /** Host primario del tenant (ej. "manfer.taxoffix.io") o null si no se pudo resolver. */
  readonly host: string | null;
  readonly inviteeKind: MeetingInviteeKind;
  readonly meetingId: string;
  readonly token: string;
  /** Fallback cuando no hay host resuelto (config.meetingInvitations.frontendBaseUrl). */
  readonly fallbackBaseUrl: string;
  /** Prefijo del portal del cliente dentro del subdominio (ej. "/portal"). */
  readonly portalPathPrefix: string;
}

/**
 * Arma el link de invitación por-tenant y por-tipo de invitado (bug prod: salía app./client.taxoffix.io
 * en vez del subdominio de la oficina):
 *  - Employee → CRM en la raíz del subdominio: `https://{host}/meetings`. El CRM no tiene ruta de
 *    join-by-token; el empleado ve el meeting en su lista (aparece por la invitación) y entra desde ahí.
 *  - Customer → Portal del cliente bajo el prefijo: `https://{host}{prefix}/client/meetings/accept/{meetingId}?token=...`,
 *    la página que pasa el token a la sala.
 *  - External (F6, D-47) → página pública de guest del CRM en la raíz del subdominio: `https://{host}/meet/{token}`
 *    (sin sesión: nombre → lobby → sala de espera).
 *
 * Si no se resolvió el host (fallo M2M), cae a `fallbackBaseUrl` con las mismas rutas: degradado pero
 * el link igual sale.
 */
export function buildMeetingJoinUrl(input: BuildMeetingJoinUrlInput): string {
  const prefix = normalizePrefix(input.portalPathPrefix);
  const fallback = input.fallbackBaseUrl.replace(/\/+$/, '');
  const staffBase = input.host ? `https://${input.host}` : fallback;
  const clientBase = input.host ? `https://${input.host}${prefix}` : `${fallback}${prefix}`;

  if (input.inviteeKind === 'Employee') {
    return `${staffBase}/meetings`;
  }
  if (input.inviteeKind === 'External') {
    return `${staffBase}/meet/${encodeURIComponent(input.token)}`;
  }
  return `${clientBase}/client/meetings/accept/${input.meetingId}?token=${encodeURIComponent(input.token)}`;
}

/** F6 (T-64): destinos genericos del recordatorio (sin token): lista de reuniones del CRM y del portal. */
export function buildMeetingListUrls(input: { host: string | null; fallbackBaseUrl: string; portalPathPrefix: string }): {
  staffJoinUrl: string;
  portalJoinUrl: string;
} {
  const prefix = normalizePrefix(input.portalPathPrefix);
  const fallback = input.fallbackBaseUrl.replace(/\/+$/, '');
  const staffBase = input.host ? `https://${input.host}` : fallback;
  const clientBase = input.host ? `https://${input.host}${prefix}` : `${fallback}${prefix}`;
  return { staffJoinUrl: `${staffBase}/meetings`, portalJoinUrl: `${clientBase}/client/meetings` };
}

/** Normaliza el prefijo del portal: sin barra final, con barra inicial; vacío queda vacío. */
function normalizePrefix(prefix: string): string {
  const trimmed = (prefix ?? '').trim().replace(/\/+$/, '');
  if (trimmed.length === 0) {
    return '';
  }
  return trimmed.startsWith('/') ? trimmed : `/${trimmed}`;
}
