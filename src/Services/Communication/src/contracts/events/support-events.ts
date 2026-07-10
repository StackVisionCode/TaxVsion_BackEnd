import type { IntegrationEvent } from './integration-event.js';

export const SupportEventTypes = {
  Opened: 'communication.support.opened.v1',
  Claimed: 'communication.support.claimed.v1',
  Resolved: 'communication.support.resolved.v1',
  Closed: 'communication.support.closed.v1',
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
}

export interface SupportResolvedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.resolved.v1';
  readonly ticketId: string;
  readonly resolvedByUserId: string;
  readonly resolvedByAgent: boolean;
}

export interface SupportClosedEvent extends IntegrationEvent {
  readonly eventType: 'communication.support.closed.v1';
  readonly ticketId: string;
  readonly closedByUserId: string;
}
