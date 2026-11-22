import type { IntegrationEvent } from './integration-event.js';

/**
 * Integration events REALMENTE publicados hoy por algun use case. Conversation
 * Archived y AttachmentUploaded todavia no se publican — viven en
 * ./pending/chat-pending-events.ts (ver nota de solapamiento con
 * AttachmentTracking ahi).
 *
 * Publicados al exchange taxvision-events. NUNCA llevan el contenido del
 * mensaje — solo IDs, timestamps y counters. Consumidores típicos:
 * Notification (email cuando destinatario offline), Analytics, Compliance/Audit.
 */
export const ChatEventTypes = {
  ConversationStarted:          'communication.chat.conversation_started.v1',
  ConversationParticipantAdded: 'communication.chat.conversation_participant_added.v1',
  ConversationParticipantRemoved: 'communication.chat.conversation_participant_removed.v1',
  MessageSent:                  'communication.chat.message_sent.v1',
  MessageEdited:                'communication.chat.message_edited.v1',
  MessageDeleted:               'communication.chat.message_deleted.v1',
  // F7 (D-27): transcript de notas de voz por el worker (salida en linea, sin archivo).
  VoiceNoteTranscriptionRequested: 'communication.chat.voice_note_transcription_requested.v1',
  VoiceNoteTranscriptReady:     'communication.chat.voice_note_transcript_ready.v1',
  VoiceNoteTranscriptFailed:    'communication.chat.voice_note_transcript_failed.v1',
  // D-N4: mensajes que siguen sin leer pasados N minutos (una vez por racha).
  MessagesMissed:               'communication.chat.messages_missed.v1',
} as const;

/** D-N4: aviso de "mensajes perdidos" para un participante. Solo ids y un contador. */
export interface ChatMessagesMissedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.messages_missed.v1';
  readonly conversationId: string;
  readonly conversationKind: 'Direct' | 'Group';
  readonly recipientUserId: string;
  readonly unreadCount: number;
  readonly oldestUnreadAtUtc: string;
  readonly latestMessageId: string;
  readonly latestSenderId: string;
}

/** F7 (D-27): pedido al worker; `fileId` es la nota de voz en CloudStorage (VoiceNotes). */
export interface VoiceNoteTranscriptionRequestedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.voice_note_transcription_requested.v1';
  readonly messageId: string;
  readonly conversationId: string;
  readonly fileId: string;
  readonly durationMs: number;
  readonly language: string;
  readonly prompt: string | null;
}

export interface ConversationStartedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.conversation_started.v1';
  readonly conversationId: string;
  readonly kind: 'Direct' | 'Group' | 'Support' | 'Meeting';
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

export interface MessageSentEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.message_sent.v1';
  readonly conversationId: string;
  readonly messageId: string;
  readonly senderId: string;
  readonly kind: 'Text' | 'Attachment' | 'System';
  readonly hasAttachment: boolean;
  readonly recipientUserIds: readonly string[];
  readonly sentAtUtc: string;
  readonly conversationKind?: 'Direct' | 'Group' | 'Support' | 'Meeting';
  readonly meetingId?: string | null;
  readonly supportTicketId?: string | null;
  /** Subconjunto de `recipientUserIds` al que conviene mandar push. Ausente = a todos (eventos viejos). */
  readonly pushRecipientUserIds?: readonly string[];
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
