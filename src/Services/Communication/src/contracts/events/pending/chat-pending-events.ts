import type { IntegrationEvent } from '../integration-event.js';

/**
 * Integration events de Chat DECLARADOS pero AUN NO publicados por ningun use
 * case (verificado por grep — cero hits fuera de este archivo). Sin fase
 * asignada todavia en el plan Backend actual; AttachmentUploaded en particular
 * puede terminar siendo redundante con el flujo ya implementado de
 * AttachmentTracking + eventos cloudstorage.file.available/infected/deleted —
 * evaluar solapamiento antes de activarlo.
 *
 * @pending sin fase asignada
 */
export const PendingChatEventTypes = {
  ConversationArchived: 'communication.chat.conversation_archived.v1',
  AttachmentUploaded: 'communication.chat.attachment_uploaded.v1',
} as const;

/** @pending sin fase asignada — requiere use case archive-conversation.ts */
export interface ConversationArchivedEvent extends IntegrationEvent {
  readonly eventType: 'communication.chat.conversation_archived.v1';
  readonly conversationId: string;
  readonly archivedByUserId: string;
  readonly archivedAtUtc: string;
}

/**
 * @pending sin fase asignada — evaluar redundancia con AttachmentTracking
 * (ya implementado: send-message.ts registra Pending, cloudstorage-consumers.ts
 * marca Available/Infected/Deleted/BlockedByPolicy). Este evento agregaria una
 * notificacion de integracion equivalente para consumidores externos
 * (Analytics/Compliance) que hoy no tienen forma de saber que se subio un
 * attachment sin consultar AttachmentTracking directamente.
 */
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
