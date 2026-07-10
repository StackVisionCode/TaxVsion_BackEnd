import type { IntegrationEvent } from './integration-event.js';

/**
 * Integration events publicados al exchange taxvision-events. Nunca llevan el
 * contenido del mensaje — solo IDs, timestamps y counters. Consumers tipicos:
 * Notification (email cuando destinatario offline), Analytics (Fase 7).
 */
export const ChatEventTypes = {
  ConversationStarted: 'communication.chat.conversation_started.v1',
  MessageSent: 'communication.chat.message_sent.v1',
} as const;

export interface ConversationStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.conversation_started.v1';
  readonly conversationId: string;
  readonly kind: 'Direct' | 'Group' | 'Support';
  readonly createdByUserId: string;
  readonly participantUserIds: readonly string[];
}

export interface MessageSentEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.message_sent.v1';
  readonly conversationId: string;
  readonly messageId: string;
  readonly senderId: string;
  readonly kind: 'Text' | 'Attachment' | 'System';
  readonly hasAttachment: boolean;
  readonly recipientUserIds: readonly string[];
  readonly sentAtUtc: string;
}
