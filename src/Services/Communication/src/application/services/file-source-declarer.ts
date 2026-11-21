import { randomUUID } from 'node:crypto';
import {
  CloudStorageEventTypes,
  COMMUNICATION_SOURCE_SERVICE,
  FileSourceResourceTypes,
  FileSourceRoles,
  type FileSourceDeclaredEvent,
} from '../../contracts/events/cloudstorage-events.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';

/** De qué recurso de Communication nace un archivo. */
export interface FileSourceDeclaration {
  readonly tenantId: string;
  readonly correlationId: string | undefined;
  readonly fileId: string;
  readonly resourceType: string;
  readonly resourceId: string;
  readonly role: string;
  readonly relatedCustomerId: string | null;
}

/** Lo mínimo de un participante para saber si es el cliente de la conversación. */
export interface ClientCandidate {
  readonly actorType: string;
  readonly customerId?: string | null | undefined;
  readonly isRemoved: boolean;
}

export function buildFileSourceDeclaredEvent(
  input: FileSourceDeclaration,
  now: Date = new Date(),
): FileSourceDeclaredEvent {
  const occurredOn = now.toISOString();
  return {
    eventId: randomUUID(),
    eventType: CloudStorageEventTypes.FileSourceDeclared,
    tenantId: input.tenantId,
    correlationId: input.correlationId,
    occurredOnUtc: occurredOn,
    occurredOn,
    fileId: input.fileId,
    sourceService: COMMUNICATION_SOURCE_SERVICE,
    sourceResourceType: input.resourceType,
    sourceResourceId: input.resourceId,
    sourceRole: input.role,
    relatedCustomerId: input.relatedCustomerId,
  };
}

/** Encola la declaración en el outbox; CloudStorage la aplica si el par encaja en su catálogo. */
export async function declareFileSource(
  publisher: Pick<IntegrationEventPublisher, 'enqueue'>,
  input: FileSourceDeclaration,
  now?: Date,
): Promise<void> {
  await publisher.enqueue(buildFileSourceDeclaredEvent(input, now));
}

/** Papel de un adjunto de chat en la declaración. */
export function chatFileRole(isVoiceNote: boolean): string {
  return isVoiceNote ? FileSourceRoles.VoiceNote : FileSourceRoles.Attachment;
}

/** El único cliente entre los participantes vigentes; con cero, varios o uno sin id conocido, null. */
export function relatedCustomerOf(participants: readonly ClientCandidate[]): string | null {
  const clients = participants.filter(
    (p) => !p.isRemoved && (p.actorType === 'CustomerPortal' || !!p.customerId),
  );
  const ids = new Set(clients.map((p) => p.customerId?.toLowerCase() ?? null));
  if (ids.size !== 1) return null;
  const [only] = ids;
  return only ?? null;
}

/** Tipo de recurso de una grabación o un transcript según la llamada o reunión dueña. */
export function recordingResourceType(scope: 'Call' | 'Meeting'): string {
  return scope === 'Call' ? FileSourceResourceTypes.Call : FileSourceResourceTypes.Meeting;
}
