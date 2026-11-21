import type { IntegrationEvent } from './integration-event.js';

/** Eventos entre Communication y CloudStorage; el nombre es el `[MessageIdentity]` de .NET. */
export const CloudStorageEventTypes = {
  FileSourceDeclared: 'cloudstorage.file_source_declared.v1',
  /** Entrante: CloudStorage pregunta de quién son archivos sin origen (`FileIds`, máx. 500). */
  FileSourcesClaimRequested: 'cloudstorage.file_sources_claim_requested.v1',
} as const;

/** Tope de ids por pedido (`FileSourcesClaimRequestedIntegrationEvent.MaxFileIds`). */
export const FILE_SOURCES_CLAIM_MAX_IDS = 500;

/** Servicio, recurso y papeles con que Communication declara sus archivos (catálogo de CloudStorage). */
export const COMMUNICATION_SOURCE_SERVICE = 'communication';

export const FileSourceResourceTypes = {
  Conversation: 'conversation',
  Call: 'call',
  Meeting: 'meeting',
} as const;

export const FileSourceRoles = {
  Attachment: 'attachment',
  VoiceNote: 'voice_note',
  Recording: 'recording',
  Transcript: 'transcript',
  TranscriptData: 'transcript_data',
} as const;

/**
 * Carga de `FileSourceDeclaredIntegrationEvent` (.NET, camelCase). Lleva `occurredOn` además del `occurredOnUtc` del
 * outbox: CloudStorage lo usa como fecha de la declaración, y sin él tomaría la hora de llegada.
 */
export interface FileSourceDeclaredEvent extends IntegrationEvent {
  readonly eventType: typeof CloudStorageEventTypes.FileSourceDeclared;
  readonly occurredOn: string;
  readonly fileId: string;
  readonly sourceService: string;
  readonly sourceResourceType: string;
  readonly sourceResourceId: string;
  readonly sourceRole: string;
  readonly relatedCustomerId: string | null;
}
