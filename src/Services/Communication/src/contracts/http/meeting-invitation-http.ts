/**
 * Contratos HTTP (request/response) para invitaciones a meetings — Fase
 * Backend 5. Declarados como interfaces puras (no Zod) porque el resto del
 * servicio define los Zod bodies inline en el propio route file (ver
 * CreateMeetingBody en api/http/routes/meetings.route.ts); los Zod schemas
 * reales viven en api/http/routes/meeting-invitations.route.ts y deben
 * mantenerse en sincro con estas formas.
 */

export interface MeetingInviteeInputDto {
  readonly kind: 'employee' | 'customer' | 'external';
  readonly userId?: string;
  readonly email?: string;
  readonly name?: string;
}

/** POST /communication/meetings/:id/invitations — Host/Cohost. */
export interface CreateMeetingInvitationsRequestDto {
  readonly invitees: readonly MeetingInviteeInputDto[];
}

export interface MeetingInvitationSummaryDto {
  readonly id: string;
  /** Solo los primeros caracteres del token — el token completo nunca se re-expone tras crearse. */
  readonly tokenPreview: string;
  readonly expiresAt: string;
  readonly inviteeEmail: string | null;
  readonly joinUrl: string;
}

export interface CreateMeetingInvitationsResponseDto {
  readonly invitations: readonly MeetingInvitationSummaryDto[];
}

/** GET /communication/meetings/:id/invitations — Host/Cohost. Nunca incluye el token (ni preview, ni hash). */
export interface MeetingInvitationListItemDto {
  readonly id: string;
  readonly inviteeKind: 'Employee' | 'Customer' | 'External';
  readonly inviteeEmail: string | null;
  readonly inviteeUserId: string | null;
  readonly inviteeName: string | null;
  readonly expiresAt: string;
  readonly usedAt: string | null;
  readonly revokedAt: string | null;
  readonly createdAt: string;
}

export interface ListMeetingInvitationsResponseDto {
  readonly invitations: readonly MeetingInvitationListItemDto[];
}

/** POST /communication/meetings/join-by-token — publico, sin auth JWT, rate-limited 5/min por IP. */
export interface JoinMeetingByTokenRequestDto {
  readonly token: string;
  readonly displayName?: string;
}

/** shortLivedJoinTicket se pasa luego como `auth.ticket` al conectar el socket. */
export interface JoinMeetingByTokenResponseDto {
  readonly shortLivedJoinTicket: string;
  readonly meetingId: string;
  readonly shortCode: string;
  readonly tenantId: string;
  readonly invitationId: string;
}

/** GET /communication/meetings/by-code/:shortCode — publico, sin exponer participantes, rate-limited 20/min por IP. */
export interface MeetingByCodeResponseDto {
  readonly title: string;
  readonly host: string;
  readonly requiresPasscode: boolean;
  readonly requiresInvitation: boolean;
}
