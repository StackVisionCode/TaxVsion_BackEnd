import { randomUUID } from 'node:crypto';
import { getRabbitContext } from '../rabbit/rabbit-connection.js';
import { config } from '../config.js';
import { logger } from '../logger.js';

/**
 * Fase D2 — reemplaza el HTTP initiate/PUT/complete a CloudStorage para subir el
 * .txt del transcript. El objeto ya se subio directo a MinIO (ver minio-uploader.ts)
 * bajo `SourceObjectKey`; este evento le pide a CloudStorage que lo registre y
 * escanee, igual que hace `SaveFileRequestedIntegrationEvent` desde Signature
 * (Fase D1) — mismo contrato, mismo tipo, distinto `RequestingService`.
 *
 * Se publica a una cola DEDICADA (no al exchange fanout `taxvision-events`) via el
 * exchange default de RabbitMQ (routingKey = nombre de cola): CloudStorage la
 * escucha con `ListenToRabbitQueue(...).DefaultIncomingMessage<SaveFileRequestedIntegrationEvent>()`,
 * que fuerza a deserializar TODO lo que llega ahi como ese tipo — mezclarlo con el
 * fanout compartido rompería cada otro evento que tambien pasa por ese exchange.
 *
 * Las claves del JSON van en PascalCase exacto (FileId, TenantId, ...) — igual que
 * las propiedades C# de `SaveFileRequestedIntegrationEvent` — para no depender de
 * que el deserializador de Wolverine sea case-insensitive.
 */

/** Fase D2/D3 — sin actor humano disponible en este contexto (el trigger es un evento de sistema, no una accion de usuario). Mismo valor reservado que usaria Notification en D3. */
const SYSTEM_ACTOR_ID = '00000000-0000-0000-0000-000000000000';

export interface SaveFileRequestedInput {
  readonly tenantId: string;
  readonly fileId: string;
  readonly sourceObjectKey: string;
  readonly ownerId: string | null;
  readonly originalName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  readonly correlationId: string | undefined;
}

export function publishSaveFileRequested(input: SaveFileRequestedInput): void {
  const rabbit = getRabbitContext();
  const eventId = randomUUID();
  const body = {
    EventId: eventId,
    TenantId: input.tenantId,
    OccurredOn: new Date().toISOString(),
    CorrelationId: input.correlationId ?? eventId,
    FileId: input.fileId,
    RequestingService: 'communication-transcript-worker',
    SourceBucket: config.minio.tempBucket,
    SourceObjectKey: input.sourceObjectKey,
    ActorId: SYSTEM_ACTOR_ID,
    OwnerType: 'Communication',
    OwnerId: input.ownerId,
    // 'Recordings' (RecordingsPolicy) solo permite .webm/.mp4 — un .txt aca
    // era rechazado siempre por whitelist de extension/content-type
    // (SaveFileRequested "rejected by upload policy"), nunca por una causa
    // transitoria. 'Transcripts' (TranscriptsPolicy) es el folder dedicado.
    FolderType: 'Transcripts',
    TaxYear: null,
    OriginalName: input.originalName,
    ContentType: input.contentType,
    SizeBytes: input.sizeBytes,
  };

  const ok = rabbit.channel.publish(
    '',
    config.cloudStorage.externalUploadsQueue,
    Buffer.from(JSON.stringify(body), 'utf-8'),
    {
      contentType: 'application/json',
      persistent: true,
      messageId: eventId,
    },
  );
  if (!ok) {
    logger.warn({ fileId: input.fileId }, 'publish backpressure — channel buffer full');
  }
}
