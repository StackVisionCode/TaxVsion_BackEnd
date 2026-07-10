import type { IntegrationEvent } from './integration-event.js';

/**
 * Integration events publicados al exchange taxvision-events. NUNCA llevan el
 * contenido del mensaje — solo IDs, timestamps y counters. Consumidores típicos:
 * Notification (email cuando destinatario offline), Analytics, Compliance/Audit.
 */
export const ChatEventTypes = {
  ConversationStarted:          'communication.chat.conversation_started.v1',
  ConversationParticipantAdded: 'communication.chat.conversation_participant_added.v1',
  ConversationParticipantRemoved: 'communication.chat.conversation_participant_removed.v1',
  ConversationArchived:         'communication.chat.conversation_archived.v1',
  MessageSent:                  'communication.chat.message_sent.v1',
  MessageEdited:                'communication.chat.message_edited.v1',
  MessageDeleted:               'communication.chat.message_deleted.v1',
  AttachmentUploaded:           'communication.chat.attachment_uploaded.v1',
} as const;

export interface ConversationStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.conversation_started.v1';
  readonly conversationId: string;
  readonly kind: 'Direct' | 'Group' | 'Support';
  readonly createdByUserId: string;
  readonly participantUserIds: readonly string[];
}

export interface ConversationParticipantAddedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.conversation_participant_added.v1';
  readonly conversationId: string;
  readonly addedByUserId: string;
  readonly newParticipantUserId: string;
}

export interface ConversationParticipantRemovedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.conversation_participant_removed.v1';
  readonly conversationId: string;
  readonly removedByUserId: string;
  readonly removedParticipantUserId: string;
  readonly reason: 'Left' | 'Kicked';
}

export interface ConversationArchivedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.conversation_archived.v1';
  readonly conversationId: string;
  readonly archivedByUserId: string;
  readonly archivedAtUtc: string;
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

export interface MessageEditedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.message_edited.v1';
  readonly conversationId: string;
  readonly messageId: string;
  readonly editedByUserId: string;
  readonly editedAtUtc: string;
}

export interface MessageDeletedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.message_deleted.v1';
  readonly conversationId: string;
  readonly messageId: string;
  readonly deletedByUserId: string;
  readonly deletedAtUtc: string;
}

export interface AttachmentUploadedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.attachment_uploaded.v1';
  readonly conversationId: string;
  readonly messageId: string;
  readonly fileId: string;
  readonly uploadedByUserId: string;
  readonly mimeType: string;
  readonly sizeBytes: number;
  readonly uploadedAtUtc: string;
}
