import type { ConsumeMessage } from 'amqplib';
import { getRabbitContext } from './rabbit-connection.js';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ProcessedEventStore } from '../../application/ports/processed-event-store.js';
import type { ConsumerHandler, IncomingEnvelope } from '../../application/ports/event-consumer.js';

/**
 * Registry sencillo de consumers por `eventType`. Cada handler valida el shape
 * del payload por su cuenta (los handlers reciben el objeto ya parseado). El
 * runtime se encarga de:
 *   - Ack durable (siempre ack, no requeue en errores no-retriables).
 *   - Idempotencia via ProcessedEventStore (inbox) — cierra CRIT-10 legacy.
 *   - Logs estructurados con eventId, eventType, source.
 *
 * Consumers se registran con `register(eventType, handler)` y arrancan con
 * `start()`. No reinventamos rueda tipo rascal: consumer set fijo por servicio.
 *
 * WIRE FORMAT — dos productores distintos, dos formatos distintos:
 *   1. Communication mismo (outbox propio, `PrismaOutboxPublisher`): el JSON
 *      del body SI incluye un campo `eventType` literal (viene de su propio
 *      contrato `IntegrationEvent` en TS). Este es el path feliz original.
 *   2. Servicios .NET (Auth/Signature/Customer/Subscription) publicando via
 *      Wolverine nativo (`options.PublishMessage<T>().ToRabbitExchange(...)`):
 *      Wolverine NO agrega ningun campo `eventType`/`EventType` al JSON body.
 *      El unico lugar donde el tipo de mensaje viaja es el header AMQP
 *      `properties.type`, con el CLR type name COMPLETO (namespace + clase),
 *      ej. "BuildingBlocks.Messaging.AuthIntegrationEvents.UserDeactivatedIntegrationEvent".
 *      Verificado empiricamente (publish de prueba + inspeccion via RabbitMQ
 *      Management API) el 2026-07-12 — antes de este fix, CADA consumer
 *      registrado para eventos .NET (auth.*, signature.*, customer.*,
 *      subscription.*) resolvia `envelope.eventType === undefined`, nunca
 *      encontraba handler, y hacia ack-and-skip silencioso en TODOS los
 *      mensajes, para siempre. `CLR_TYPE_TO_EVENT_TYPE` traduce ese header a
 *      la string dotted-lowercase que los `bindXxxConsumers` ya registran.
 */
const CLR_TYPE_TO_EVENT_TYPE: Readonly<Record<string, string>> = {
  // Auth
  'TaxVision.Auth.Application.Users.IntegrationEvents.UserRegisteredIntegrationEvent': 'auth.user.registered.v1',
  'BuildingBlocks.Messaging.AuthIntegrationEvents.UserRolesChangedIntegrationEvent': 'auth.user.roles_changed.v1',
  'BuildingBlocks.Messaging.AuthIntegrationEvents.UserDeactivatedIntegrationEvent': 'auth.user.deactivated.v1',
  'BuildingBlocks.Messaging.AuthIntegrationEvents.UserProfileUpdatedIntegrationEvent': 'auth.user.profile_updated.v1',
  // Customer
  'BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomersBulkImportedIntegrationEvent': 'customer.bulk_imported.v1',
  // Fase Backend 10 — alimentan CustomerDirectoryEntry (ver customer-consumers.ts).
  'BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomerCreatedIntegrationEvent': 'customer.created.v1',
  'BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomerUpdatedIntegrationEvent': 'customer.updated.v1',
  'BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomerDeactivatedIntegrationEvent': 'customer.deactivated.v1',
  // Signature
  'BuildingBlocks.Messaging.SignatureIntegrationEvents.SignerInvitedIntegrationEvent': 'signature.signer.invited.v1',
  'BuildingBlocks.Messaging.SignatureIntegrationEvents.DocumentSignedIntegrationEvent': 'signature.document.signed.v1',
  'BuildingBlocks.Messaging.SignatureIntegrationEvents.SignatureRequestCompletedIntegrationEvent':
    'signature.request.completed.v1',
  'BuildingBlocks.Messaging.SignatureIntegrationEvents.SignatureRequestCanceledIntegrationEvent':
    'signature.request.canceled.v1',
  'BuildingBlocks.Messaging.SignatureIntegrationEvents.SignatureRequestReminderDueIntegrationEvent':
    'signature.request.reminder_due.v1',
  'BuildingBlocks.Messaging.SignatureIntegrationEvents.SignatureRequestSealedIntegrationEvent':
    'signature.request.sealed.v1',
  'BuildingBlocks.Messaging.SignatureIntegrationEvents.SignerVerificationChallengeIssuedIntegrationEvent':
    'signature.signer.verification.challenge_issued.v1',
  // Subscription — evento unico de "algo cambio en la suscripcion" (reemplaza a los
  // antiguos activated/plan_changed/seats_purchased/suspended, retirados en la fase de
  // cleanup del rediseno de Subscription, 2026-07).
  'BuildingBlocks.Messaging.SubscriptionIntegrationEvents.TenantEntitlementsChangedIntegrationEvent':
    'subscription.entitlements_changed.v1',
  // CloudStorage
  'BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FileAvailableIntegrationEvent':
    'cloudstorage.file.available.v1',
  'BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FileInfectedDetectedIntegrationEvent':
    'cloudstorage.file.infected.v1',
  'BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FileDeletedIntegrationEvent':
    'cloudstorage.file.deleted.v1',
  'BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FileBlockedByPolicyIntegrationEvent':
    'cloudstorage.file.blocked_by_policy.v1',
  'BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FilePendingReviewIntegrationEvent':
    'cloudstorage.file.pending_review.v1',
};

export type { ConsumerHandler, IncomingEnvelope };

export class ConsumerRuntime {
  private handlers = new Map<string, ConsumerHandler>();
  private consumerTag: string | undefined;
  private inFlightCount = 0;

  constructor(private readonly processedEvents: ProcessedEventStore) {}

  register(eventType: string, handler: ConsumerHandler): void {
    if (this.handlers.has(eventType)) {
      throw new Error(`Duplicate consumer for ${eventType}`);
    }
    this.handlers.set(eventType, handler);
  }

  async start(): Promise<void> {
    const rabbit = getRabbitContext();
    await rabbit.channel.prefetch(20);
    const { consumerTag } = await rabbit.channel.consume(config.rabbitmq.queue, (msg) => {
      if (!msg) return;
      void this.dispatch(msg);
    });
    this.consumerTag = consumerTag;
    logger.info({ queue: config.rabbitmq.queue, handlers: this.handlers.size }, 'consumer runtime started');
  }

  /**
   * Fase Backend 11 — graceful drain en SIGTERM. `channel.cancel` deja de
   * entregar mensajes NUEVOS de inmediato (RabbitMQ los redirige a otro
   * consumer si hay uno, o los deja en la cola); lo que sigue es esperar a
   * que los `dispatch()` YA en curso (contados via `inFlightCount`) terminen
   * de correr su handler y hacer ack/nack, en vez de matarlos a mitad de
   * camino. Si el timeout se cumple igual, logueamos cuantos quedaron sin
   * drenar — el proceso va a terminar igual (main.ts sigue con el resto del
   * shutdown), esos mensajes vuelven a entregarse al reconectar el consumer.
   */
  async stop(drainTimeoutMs = 30_000): Promise<void> {
    if (this.consumerTag) {
      try {
        const rabbit = getRabbitContext();
        await rabbit.channel.cancel(this.consumerTag);
      } catch (err) {
        logger.warn({ err: (err as Error).message }, 'ConsumerRuntime.stop: channel.cancel failed');
      }
    }
    const deadline = Date.now() + drainTimeoutMs;
    while (this.inFlightCount > 0 && Date.now() < deadline) {
      await new Promise((resolve) => setTimeout(resolve, 100));
    }
    if (this.inFlightCount > 0) {
      logger.warn(
        { inFlight: this.inFlightCount, drainTimeoutMs },
        'ConsumerRuntime.stop: drain timeout exceeded, messages still in flight',
      );
    } else {
      logger.info('ConsumerRuntime.stop: drained cleanly');
    }
  }

  private async dispatch(msg: ConsumeMessage): Promise<void> {
    this.inFlightCount += 1;
    try {
      await this.dispatchInner(msg);
    } finally {
      this.inFlightCount -= 1;
    }
  }

  private async dispatchInner(msg: ConsumeMessage): Promise<void> {
    const rabbit = getRabbitContext();
    const raw = msg.content.toString('utf-8');
    let envelope: IncomingEnvelope;
    try {
      const parsed = JSON.parse(raw) as Partial<IncomingEnvelope> & { [k: string]: unknown };
      // AMQP `type` property: Wolverine-published .NET messages carry the CLR
      // type name here instead of an `eventType` field in the JSON body. See
      // the module docblock above for why this fallback exists.
      const amqpTypeHeader = typeof msg.properties.type === 'string' ? msg.properties.type : undefined;
      envelope = this.normalizeEnvelope(parsed, amqpTypeHeader);
      if (amqpTypeHeader && !envelope.eventType) {
        // A .NET-originated message we don't have a CLR_TYPE_TO_EVENT_TYPE
        // mapping for — surfaces new event types immediately instead of
        // silently ack-skipping forever (the bug this fallback fixed).
        logger.warn({ amqpTypeHeader }, 'consumer: unmapped CLR type in AMQP type header; ack to skip');
      }
    } catch (err) {
      logger.warn({ err: (err as Error).message }, 'consumer: unparseable payload; ack to skip');
      rabbit.channel.ack(msg);
      return;
    }

    const handler = this.handlers.get(envelope.eventType);
    if (!handler) {
      rabbit.channel.ack(msg);
      return;
    }

    const fresh = await this.processedEvents.tryMarkProcessed({
      eventId: envelope.eventId,
      source: this.extractSource(envelope.eventType),
      eventType: envelope.eventType,
      tenantId: envelope.tenantId,
    });
    if (!fresh) {
      logger.debug({ eventId: envelope.eventId, eventType: envelope.eventType }, 'inbox: duplicate; skip');
      rabbit.channel.ack(msg);
      return;
    }

    try {
      await handler(envelope);
      rabbit.channel.ack(msg);
    } catch (err) {
      logger.error(
        { err: (err as Error).message, eventId: envelope.eventId, eventType: envelope.eventType },
        'consumer handler failed — sending to DLQ',
      );
      // Rollback the inbox mark so a retry from the DLQ (manual reprocess) is
      // not treated as a duplicate. If the DB write fails, log and continue —
      // the message is going to the DLQ either way.
      await this.processedEvents
        .unmark({
          eventId: envelope.eventId,
          source: this.extractSource(envelope.eventType),
        })
        .catch((unmarkErr: unknown) =>
          logger.warn({ err: (unmarkErr as Error).message }, 'inbox unmark after handler failure failed'),
        );
      // nack with requeue=false → dead-letter routing (see rabbit-connection.ts:
      // main queue has deadLetterExchange:'' + deadLetterRoutingKey:dlq).
      rabbit.channel.nack(msg, false, false);
    }
  }

  private normalizeEnvelope(
    raw: Partial<IncomingEnvelope> & Record<string, unknown>,
    amqpTypeHeader?: string,
  ): IncomingEnvelope {
    const eventId = typeof raw['eventId'] === 'string' ? (raw['eventId'] as string) : (raw['EventId'] as string);
    const bodyEventType =
      typeof raw['eventType'] === 'string' ? (raw['eventType'] as string) : (raw['EventType'] as string | undefined);
    const eventType = (bodyEventType ??
      (amqpTypeHeader ? CLR_TYPE_TO_EVENT_TYPE[amqpTypeHeader] : undefined)) as string;
    const tenantId = typeof raw['tenantId'] === 'string' ? (raw['tenantId'] as string) : (raw['TenantId'] as string);
    const occurredOnUtc =
      typeof raw['occurredOnUtc'] === 'string'
        ? (raw['occurredOnUtc'] as string)
        : typeof raw['OccurredOn'] === 'string'
          ? (raw['OccurredOn'] as string)
          : new Date().toISOString();
    const correlationId =
      typeof raw['correlationId'] === 'string'
        ? (raw['correlationId'] as string)
        : typeof raw['CorrelationId'] === 'string'
          ? (raw['CorrelationId'] as string)
          : undefined;
    return {
      eventId,
      eventType,
      tenantId,
      occurredOnUtc,
      ...(correlationId !== undefined ? { correlationId } : {}),
      // El resto del payload queda accesible con la misma forma que llego.
      payload: raw as Readonly<Record<string, unknown>>,
    };
  }

  private extractSource(eventType: string): string {
    // "signature.request.reminder_due.v1" → "signature"
    const idx = eventType.indexOf('.');
    return idx > 0 ? eventType.slice(0, idx) : eventType;
  }
}
