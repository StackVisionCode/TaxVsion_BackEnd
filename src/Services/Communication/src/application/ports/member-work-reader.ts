import type { SupportStatus } from '../../domain/support/support-enums.js';

/** Ticket no terminal asignado a un agente. Nunca trae mensajes del chat. */
export interface AssignedTicketRow {
  readonly id: string;
  readonly subject: string;
  readonly status: SupportStatus;
  readonly assignedAgentId: string;
  readonly openedAtUtc: Date;
}

/** Reunión próxima (Live, o Scheduled desde ahora) con lo justo para decidir membresía y visibilidad. */
export interface UpcomingMeetingRow {
  readonly id: string;
  readonly title: string;
  readonly status: 'Scheduled' | 'Live';
  readonly hostUserId: string;
  readonly scheduledForUtc: Date | null;
  readonly participants: readonly { readonly userId: string; readonly status: string }[];
  readonly invitees: readonly {
    readonly userId: string;
    readonly revoked: boolean;
    readonly closed: boolean;
  }[];
}

/** Lectura del Work Profile: trabajo a cargo de varias personas en una oficina, en una sola consulta por tipo. */
export interface MemberWorkReader {
  /** Tickets no Resolved/Closed asignados a esos agentes, los más antiguos primero. */
  listOpenAssignedTickets(agentTenantId: string, userIds: readonly string[]): Promise<AssignedTicketRow[]>;
  /** Reuniones Live o Scheduled desde `nowUtc` donde alguno es host, participante o invitado. */
  listUpcomingMeetings(
    tenantId: string,
    userIds: readonly string[],
    nowUtc: Date,
  ): Promise<UpcomingMeetingRow[]>;
}
