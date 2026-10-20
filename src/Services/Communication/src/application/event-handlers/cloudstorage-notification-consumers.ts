import { pushAndEmitNotification, type BellNotificationInput } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';

/**
 * Consumers de CloudStorage -> notificaciones in-app para el DUEÑO del recurso
 * (`CreatedBy`/`CreatedByUserId`).
 *
 * Separado de `cloudstorage-consumers.ts` a proposito: ese archivo tiene una
 * responsabilidad distinta (mantener al dia `AttachmentTracking` para
 * adjuntos de chat) y ya registra handlers para `cloudstorage.file.available.v1`
 * y `cloudstorage.file.blocked_by_policy.v1` — el `ConsumerRuntime` solo admite
 * UN handler por `eventType` (`register()` lanza en duplicado), asi que la
 * notificacion de bloqueo vive DENTRO de `cloudstorage-consumers.ts`.
 *
 * `DmcaCounterNoticeSubmittedIntegrationEvent` NO se consume aca (ver
 * consumer-runtime.ts): no tiene un destinatario individual — requiere el
 * fan-out por rol de la Fase 4, todavia no construido.
 */
export function bindCloudStorageNotificationConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    notifications: NotificationRepository;
    emitter: RealtimeEmitter;
    inAppSource: 'Communication' | 'Notification';
  },
): void {
  if (deps.inAppSource === 'Notification') return;
  register('cloudstorage.sharelink.folder_item_added.v1', (env) => folderItemAddedHandler(env, deps));
  register('cloudstorage.file.blocked_by_dmca_takedown.v1', (env) => dmcaTakedownHandler(env, deps));
  register('cloudstorage.file.reinstated_from_takedown.v1', (env) => dmcaReinstatedHandler(env, deps));
  register('cloudstorage.file.legal_hold_placed.v1', (env) => legalHoldPlacedHandler(env, deps));
  register('cloudstorage.file.legal_hold_lifted.v1', (env) => legalHoldLiftedHandler(env, deps));
}

async function folderItemAddedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const shareLinkId = getString(env.payload, 'shareLinkId') ?? getString(env.payload, 'ShareLinkId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const autoCovered =
    getBoolean(env.payload, 'autoCovered') ?? getBoolean(env.payload, 'AutoCovered') ?? false;
  if (!fileId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'cloudstorage.sharelink.folder_item_added',
    priority: 'High',
    title: 'File moved into a shared folder',
    body: autoCovered
      ? 'A file of yours became accessible through an existing public link after moving folders.'
      : 'A file of yours moved into a folder that has a public link (not yet covered by that link).',
    metadata: { fileId, shareLinkId },
    category: 'DocumentsStorage',
    actorUserId: null,
    actorType: 'System',
    resourceType: 'file',
    resourceId: fileId,
    ...(shareLinkId ? { resourceContext: { shareLinkId } } : {}),
    action: 'review',
    occurredAtUtc: envOccurredAt(env),
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
    title: 'File blocked by a DMCA notice',
    body: 'One of your files was blocked after a copyright claim was received.',
    metadata: { fileId },
    category: 'DocumentsStorage',
    actorUserId: null,
    actorType: 'System',
    resourceType: 'file',
    resourceId: fileId,
    action: 'review',
    occurredAtUtc: envOccurredAt(env),
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
    title: 'File access restored',
    body: 'The legal team reinstated one of your files after resolving a DMCA claim.',
    metadata: { fileId },
    category: 'DocumentsStorage',
    actorUserId: null,
    actorType: 'System',
    resourceType: 'file',
    resourceId: fileId,
    action: 'open',
    occurredAtUtc: envOccurredAt(env),
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
    title: 'File placed on legal hold',
    body: 'One of your files is under a legal hold and cannot be deleted while it lasts.',
    metadata: { fileId },
    category: 'DocumentsStorage',
    actorUserId: null,
    actorType: 'System',
    resourceType: 'file',
    resourceId: fileId,
    action: 'review',
    occurredAtUtc: envOccurredAt(env),
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
    title: 'Legal hold lifted',
    body: 'The legal hold on one of your files was lifted.',
    metadata: { fileId },
    category: 'DocumentsStorage',
    actorUserId: null,
    actorType: 'System',
    resourceType: 'file',
    resourceId: fileId,
    action: 'open',
    occurredAtUtc: envOccurredAt(env),
  }, deps);
}

async function push(
  env: IncomingEnvelope,
  input: BellNotificationInput,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  await pushAndEmitNotification(env, input, deps);
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}

function getBoolean(source: Record<string, unknown>, key: string): boolean | undefined {
  const value = source[key];
  return typeof value === 'boolean' ? value : undefined;
}

/**
 * `OccurredOn` del envelope .NET: hora REAL del hecho (p.ej. cuándo CloudStorage
 * aplicó la retención), distinta de `CreatedAtUtc` del repo que es la hora de
 * recepción. Fallback a now() si el publisher no lo mandó o es inválido.
 */
function envOccurredAt(env: IncomingEnvelope): Date {
  const raw = getString(env.payload, 'occurredOn') ?? getString(env.payload, 'OccurredOn');
  if (!raw) return new Date();
  const parsed = new Date(raw);
  return Number.isFinite(parsed.getTime()) ? parsed : new Date();
}
