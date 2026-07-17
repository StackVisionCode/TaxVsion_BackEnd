import type { Meeting, MeetingSnapshot } from '../../domain/meetings/meeting.js';
import type { MeetingInvitation } from '../../domain/meetings/meeting-invitation.js';

export interface MeetingRepository {
  save(meeting: Meeting): Promise<void>;
  findById(tenantId: string, meetingId: string): Promise<Meeting | null>;
  findByShortCode(tenantId: string, shortCode: string): Promise<Meeting | null>;
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
  listUpcomingForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
  }): Promise<MeetingSnapshot[]>;
  countUpcomingForUser(tenantId: string, userId: string): Promise<number>;
  /** Fase Frontend 9 — "historial" (Ended/Cancelled), separado de upcoming (Scheduled/Live). */
  listPastForUser(input: {
    tenantId: string;
    userId: string;
    take: number;
    skip: number;
  }): Promise<MeetingSnapshot[]>;
  countPastForUser(tenantId: string, userId: string): Promise<number>;
}
