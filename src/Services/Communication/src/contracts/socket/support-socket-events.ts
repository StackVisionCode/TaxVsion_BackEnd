export const SupportSocketEvents = {
  TicketOpened: 'support.ticket.opened',
  TicketChanged: 'support.ticket.changed',
  TicketClaimed: 'support.ticket.claimed',
  TicketReassigned: 'support.ticket.reassigned',
  TicketEscalated: 'support.ticket.escalated',
  TicketResolved: 'support.ticket.resolved',
  TicketClosed: 'support.ticket.closed',
  TicketReopened: 'support.ticket.reopened',
} as const;

export interface SupportTicketRealtimeDto {
  readonly ticketId: string;
  readonly tenantId: string;
  readonly agentTenantId: string;
  readonly supportRecipientUserIds?: readonly string[];
  readonly conversationId?: string;
  readonly subject?: string;
  readonly category?: string;
  readonly priority?: string;
  readonly status?: string;
  readonly assignedAgentId?: string | null;
  readonly changedByUserId?: string;
  readonly changeType: 'opened' | 'claimed' | 'reassigned' | 'escalated' | 'resolved' | 'closed' | 'reopened';
  readonly occurredOnUtc: string;
}
