import type { IntegrationEvent } from './integration-event.js';

/**
 * Integration events del ciclo de vida de un ticket de soporte cross-tenant.
 * El canal support es PlatformTenant (TaxVision) ↔ office tenant (preparadores).
 * NUNCA cruza preparador ↔ contribuyente.
 */
export const SupportEventTypes = {
  Opened:           'communication.support.opened.v1',
  Claimed:          'communication.support.claimed.v1',
  Reassigned:       'communication.support.reassigned.v1',
  Escalated:        'communication.support.escalated.v1',
  FirstResponseSet: 'communication.support.first_response_set.v1',
  MessageAdded:     'communication.support.message_added.v1',
  Resolved:         'communication.support.resolved.v1',
  Closed:           'communication.support.closed.v1',
  Reopened:         'communication.support.reopened.v1',
} as const;

export interface SupportOpenedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.opened.v1';
  readonly ticketId: string;
  readonly agentTenantId: string;
  readonly openedByUserId: string;
  readonly conversationId: string;
  readonly subject: string;
  readonly category: 'Billing' | 'Technical' | 'Account' | 'Other';
  readonly priority: 'Low' | 'Normal' | 'High' | 'Urgent';
}

export interface SupportClaimedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.claimed.v1';
  readonly ticketId: string;
  readonly assignedAgentId: string;
  readonly claimedAtUtc: string;
}

export interface SupportReassignedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.reassigned.v1';
  readonly ticketId: string;
  readonly previousAgentId: string;
  readonly newAgentId: string;
  readonly reassignedByUserId: string;
  readonly reassignedAtUtc: string;
}

export interface SupportEscalatedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.escalated.v1';
  readonly ticketId: string;
  readonly escalatedByUserId: string;
  readonly previousPriority: 'Low' | 'Normal' | 'High' | 'Urgent';
  readonly newPriority: 'Low' | 'Normal' | 'High' | 'Urgent';
  readonly escalatedAtUtc: string;
}

export interface SupportFirstResponseSetEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.first_response_set.v1';
  readonly ticketId: string;
  readonly respondingAgentId: string;
  readonly openedAtUtc: string;
  readonly firstResponseAtUtc: string;
  /** Segundos entre Opened y primera respuesta del agente — métrica SLA. */
  readonly responseTimeSeconds: number;
}

export interface SupportMessageAddedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.message_added.v1';
  readonly ticketId: string;
  readonly messageId: string;
  readonly senderUserId: string;
  readonly senderRole: 'Customer' | 'Agent';
  readonly sentAtUtc: string;
}

export interface SupportResolvedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.resolved.v1';
  readonly ticketId: string;
  readonly resolvedByUserId: string;
  readonly resolvedByAgent: boolean;
  readonly resolvedAtUtc: string;
}

export interface SupportClosedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.closed.v1';
  readonly ticketId: string;
  readonly closedByUserId: string;
  readonly closedAtUtc: string;
}

export interface SupportReopenedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.reopened.v1';
  readonly ticketId: string;
  readonly reopenedByUserId: string;
  readonly reopenedAtUtc: string;
  readonly reason: string | null;
}
