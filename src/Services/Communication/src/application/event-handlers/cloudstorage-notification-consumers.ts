import { randomUUID } from 'node:crypto';
import { pushNotification, type PushNotificationCommand } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';

/**
 * Consumers de CloudStorage -> notificaciones in-app para el DUEÑO del recurso
 * (`CreatedBy`/`CreatedByUserId`, poblado en la Fase 1B del plan de
 * notificaciones dinamicas — antes estos 8 eventos no llevaban ningun actor).
 *
 * Separado de `cloudstorage-consumers.ts` a proposito: ese archivo tiene una
 * responsabilidad distinta (mantener al dia `AttachmentTracking` para
 * adjuntos de chat) y ya registra handlers para `cloudstorage.file.available.v1`
 * y `cloudstorage.file.blocked_by_policy.v1` — el `ConsumerRuntime` solo admite
 * UN handler por `eventType` (`register()` lanza en duplicado), asi que la
 * notificacion de esos 2 eventos vive DENTRO de `cloudstorage-consumers.ts`
 * (ver su docblock), no aca. Este archivo cubre los 8 eventos restantes que no
 * tienen ningun otro consumer todavia.
 *
 * `DmcaCounterNoticeSubmittedIntegrationEvent` NO se consume aca (ver
 * consumer-runtime.ts): no tiene un destinatario individual — requiere el
 * fan-out por rol de la Fase 4, todavia no construido.
 */
export function bindCloudStorageNotificationConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): void {
  register('cloudstorage.file.restored.v1', (env) => fileRestoredHandler(env, deps));
  register('cloudstorage.sharelink.folder_item_added.v1', (env) => folderItemAddedHandler(env, deps));
  register('cloudstorage.sharelink.accessed.v1', (env) => shareAccessedHandler(env, deps));
  register('cloudstorage.sharelink.expired.v1', (env) => shareExpiredHandler(env, deps));
  register('cloudstorage.file.blocked_by_dmca_takedown.v1', (env) => dmcaTakedownHandler(env, deps));
  register('cloudstorage.file.reinstated_from_takedown.v1', (env) => dmcaReinstatedHandler(env, deps));
  register('cloudstorage.file.legal_hold_placed.v1', (env) => legalHoldPlacedHandler(env, deps));
  register('cloudstorage.file.legal_hold_lifted.v1', (env) => legalHoldLiftedHandler(env, deps));
}

async function fileRestoredHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const createdBy = getString(env.payload, 'createdBy') ?? getString(env.payload, 'CreatedBy');
  if (!fileId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'cloudstorage.file.restored',
    priority: 'Normal',
    title: 'Archivo restaurado',
    body: 'Un archivo tuyo fue restaurado desde la papelera.',
    metadata: { fileId },
  }, deps);
}

async function folderItemAddedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const shareLinkId = getString(env.payload, 'shareLinkId') ?? getString(env.payload, 'ShareLinkId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const autoCovered = getBoolean(env.payload, 'autoCovered') ?? getBoolean(env.payload, 'AutoCovered') ?? false;
  if (!fileId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'cloudstorage.sharelink.folder_item_added',
    priority: 'High',
    title: 'Archivo movido a una carpeta compartida',
    body: autoCovered
      ? 'Un archivo tuyo quedo accesible por un link publico existente al moverse de carpeta.'
      : 'Un archivo tuyo se movio a una carpeta con un link publico (todavia no cubierto por ese link).',
    metadata: { fileId, shareLinkId },
  }, deps);
}

async function shareAccessedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const channel = getString(env.payload, 'channel') ?? getString(env.payload, 'Channel') ?? '';
  if (!fileId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'cloudstorage.sharelink.accessed',
    priority: 'Low',
    title: 'Tu archivo compartido fue accedido',
    body: 'Alguien accedio a un archivo tuyo a traves de un link de compartir.',
    metadata: { fileId, channel },
  }, deps);
}

async function shareExpiredHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const shareLinkId = getString(env.payload, 'shareLinkId') ?? getString(env.payload, 'ShareLinkId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!shareLinkId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'cloudstorage.sharelink.expired',
    priority: 'Normal',
    title: 'Link de compartir vencido',
    body: 'Un link de compartir que creaste ya vencio.',
    metadata: { shareLinkId },
  }, deps);
}

async function dmcaTakedownHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const createdBy = getString(env.payload, 'createdBy') ?? getString(env.payload, 'CreatedBy');
  if (!fileId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'cloudstorage.file.blocked_by_dmca_takedown',
    priority: 'High',
    title: 'Archivo bloqueado por reclamo DMCA',
    body: 'Uno de tus archivos fue bloqueado tras recibirse un reclamo de derechos de autor.',
    metadata: { fileId },
  }, deps);
}

async function dmcaReinstatedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const createdBy = getString(env.payload, 'createdBy') ?? getString(env.payload, 'CreatedBy');
  if (!fileId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'cloudstorage.file.reinstated_from_takedown',
    priority: 'Normal',
    title: 'Archivo reinstalado',
    body: 'El equipo legal reinstalo un archivo tuyo tras resolver un reclamo DMCA.',
    metadata: { fileId },
  }, deps);
}

async function legalHoldPlacedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const createdBy = getString(env.payload, 'createdBy') ?? getString(env.payload, 'CreatedBy');
  if (!fileId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'cloudstorage.file.legal_hold_placed',
    priority: 'Normal',
    title: 'Archivo en retencion legal',
    body: 'Uno de tus archivos quedo bajo retencion legal y no puede borrarse mientras dure.',
    metadata: { fileId },
  }, deps);
}

async function legalHoldLiftedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const createdBy = getString(env.payload, 'createdBy') ?? getString(env.payload, 'CreatedBy');
  if (!fileId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'cloudstorage.file.legal_hold_lifted',
    priority: 'Normal',
    title: 'Retencion legal levantada',
    body: 'La retencion legal sobre uno de tus archivos fue levantada.',
    metadata: { fileId },
  }, deps);
}

async function push(
  env: IncomingEnvelope,
  input: Pick<PushNotificationCommand, 'userId' | 'kind' | 'priority' | 'title' | 'body' | 'metadata'>,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const result = await pushNotification(
    {
      tenantId: env.tenantId,
      userId: input.userId,
      kind: input.kind,
      ...(input.priority !== undefined ? { priority: input.priority } : {}),
      title: input.title,
      body: input.body,
      ...(input.metadata !== undefined ? { metadata: input.metadata } : {}),
      sourceEventId: env.eventId,
      sourceEventType: env.eventType,
      correlationId: env.correlationId ?? null,
    },
    deps,
  );
  if (result.isSuccess && result.value.created && result.value.notification) {
    deps.emitter.emitToUser({
      tenantId: env.tenantId,
      userId: input.userId,
      event: NotificationSocketEvents.Received,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: result.value.notification,
      },
    });
    deps.emitter.emitToUser({
      tenantId: env.tenantId,
      userId: input.userId,
      event: NotificationSocketEvents.UnreadCountChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: { count: result.value.unreadCount },
      },
    });
  }
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}

function getBoolean(source: Record<string, unknown>, key: string): boolean | undefined {
  const value = source[key];
  return typeof value === 'boolean' ? value : undefined;
}
