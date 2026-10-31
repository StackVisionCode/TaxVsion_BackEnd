import { randomUUID } from 'node:crypto';
import type { FileDeletionRequester, FileDeletionTarget } from '../../application/ports/file-deletion-requester.js';
import { FILES_DELETION_QUEUE, FILES_DELETION_REQUESTED_TYPE } from '../../contracts/events/recording-events.js';
import { getRabbitContext } from './rabbit-connection.js';
import { logger } from '../logger/logger.js';

/**
 * Publica el contrato .NET existente `FilesDeletionRequestedIntegrationEvent` punto a punto a la cola
 * `cloudstorage-file-deletions` (exchange por defecto, routingKey = cola). Cuerpo en PascalCase como
 * las propiedades C#; el `type` AMQP es el nombre completo del tipo, que es como Wolverine lo resuelve
 * cuando el contrato no declara `[MessageIdentity]`.
 */
export class RabbitFileDeletionRequester implements FileDeletionRequester {
  async requestDeletion(input: {
    tenantId: string;
    sourceReferenceId: string;
    reason: string;
    correlationId: string;
    files: readonly FileDeletionTarget[];
  }): Promise<void> {
    if (input.files.length === 0) return;
    const eventId = randomUUID();
    const body = {
      EventId: eventId,
      TenantId: input.tenantId,
      OccurredOn: new Date().toISOString(),
      CorrelationId: input.correlationId,
      SourceService: 'communication.recordings',
      SourceReferenceId: input.sourceReferenceId,
      Reason: input.reason,
      Files: input.files.map((f) => ({
        FileId: f.fileId,
        ExpectedOwnerType: f.ownerType,
        ExpectedOwnerId: f.ownerId,
        ExpectedFolderType: f.folderType,
      })),
    };
    const ok = getRabbitContext().channel.sendToQueue(FILES_DELETION_QUEUE, Buffer.from(JSON.stringify(body), 'utf-8'), {
      contentType: 'application/json',
      persistent: true,
      messageId: eventId,
      type: FILES_DELETION_REQUESTED_TYPE,
    });
    // `false` = buffer del canal lleno: el mensaje igual queda encolado en el cliente; solo se avisa.
    if (!ok) logger.warn({ sourceReferenceId: input.sourceReferenceId }, 'file deletion publish backpressure');
  }
}
