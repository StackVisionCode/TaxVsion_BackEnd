import type { Meeting, MeetingSnapshot } from '../../domain/meetings/meeting.js';
import type { MeetingInvitation } from '../../domain/meetings/meeting-invitation.js';

/** Listados por usuario; `customerId`/`counterpartUserId` acotan al perfil de un cliente (solo staff). */
export interface MeetingsForUserQuery {
  readonly tenantId: string;
  readonly userId: string;
  /** D-58: columna `CustomerId` del participante; sobrevive a la baja de la cuenta de portal. */
  readonly customerId?: string;
  /** Usuario de portal activo del cliente: cubre invitaciones sin usar y filas previas al backfill. */
  readonly counterpartUserId?: string;
}

export interface MeetingRepository {
  save(meeting: Meeting): Promise<void>;
  findById(tenantId: string, meetingId: string): Promise<Meeting | null>;
  findByShortCode(tenantId: string, shortCode: string): Promise<Meeting | null>;
  /**
   * Reuniones activas (Scheduled|Live) cuyo host es `hostUserId` — para el handover al retirar
   * a ese empleado (offboarding). Devuelve el aggregate completo porque se van a mutar y guardar.
   */
  listActiveHostedBy(tenantId: string, hostUserId: string): Promise<Meeting[]>;
  /** Cuántas reuniones activas (Scheduled|Live) tiene como host — pre-flight de impacto al retirarlo. */
  countActiveHostedBy(tenantId: string, hostUserId: string): Promise<number>;
  /** F3 — scheduler de ciclo de vida: todas las reuniones Live de la plataforma (lote acotado). */
  listLive(input: { take: number }): Promise<Meeting[]>;
  /** F3 — Scheduled con fecha (o creación, si no tienen fecha) anterior al corte: candidatas a Expired. */
  listScheduledBefore(input: { cutoffUtc: Date; take: number }): Promise<Meeting[]>;
  /** F6 (T-64) — Scheduled con fecha hasta `untilUtc` y sin recordatorio enviado (lote acotado). */
  listScheduledForReminder(input: { untilUtc: Date; take: number }): Promise<Meeting[]>;
  /**
   * Cross-tenant lookup por diseno — Fase Backend 5, usado por el endpoint
   * publico GET /communication/meetings/by-code/:shortCode (sin JWT, sin
   * contexto de tenant resuelto). ShortCode solo es unico POR tenant
   * (`@@unique([TenantId, ShortCode])`), pero con 9 chars de un alfabeto de 32
   * (~10^13 combinaciones) la colision cross-tenant es estadisticamente
   * despreciable — mismo criterio de "unico en la practica" que
   * findInvitationByHash aplica al TokenHash.
   */
  findByShortCodeAnyTenant(shortCode: string): Promise<Meeting | null>;
  saveInvitation(invitation: MeetingInvitation): Promise<void>;
  /**
   * Cross-tenant lookup por diseno: el TokenHash es globalmente unico (SHA-256
   * de 32 bytes cripto-random). Se necesita para validar la invitation antes
   * de conocer el tenant del meeting. Cierre CRIT-6 legacy — nunca se persiste
   * el JWT en claro, solo el hash.
   */
  findInvitationByHash(tokenHash: string): Promise<MeetingInvitation | null>;
  /** Fase Backend 5 — usado por el guest join (join-meeting.ts) para re-cargar por id, ya sin el plainToken. */
  findInvitationById(tenantId: string, invitationId: string): Promise<MeetingInvitation | null>;
  /** Fase Backend 5 — listado/revocacion por Host/Cohost (GET/DELETE .../invitations). */
  listInvitationsByMeeting(tenantId: string, meetingId: string): Promise<MeetingInvitation[]>;
  listUpcomingForUser(input: MeetingsForUserQuery & { take: number; skip: number }): Promise<MeetingSnapshot[]>;
  countUpcomingForUser(input: MeetingsForUserQuery): Promise<number>;
  /** Fase Frontend 9 — "historial" (Ended/Cancelled/Expired), separado de upcoming (Scheduled/Live). */
  listPastForUser(input: MeetingsForUserQuery & { take: number; skip: number }): Promise<MeetingSnapshot[]>;
  countPastForUser(input: MeetingsForUserQuery): Promise<number>;
  /**
   * Contadores para las tarjetas del dashboard de meetings, calculados sobre TODOS los meetings del
   * usuario (no la página cargada en el cliente). Las fronteras de día/semana vienen ya resueltas a UTC
   * desde la zona horaria del usuario (el caller las calcula con `zoneinfo`).
   */
  getStatsForUser(input: {
    tenantId: string;
    userId: string;
    nowUtc: Date;
    dayStartUtc: Date;
    dayEndUtc: Date;
    weekEndUtc: Date;
  }): Promise<MeetingStatsForUser>;
}

export interface MeetingStatsForUser {
  readonly today: number;
  readonly thisWeek: number;
  readonly liveNow: number;
  readonly transcriptsAvailable: number;
}
