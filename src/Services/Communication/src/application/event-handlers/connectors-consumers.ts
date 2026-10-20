import { pushAndEmitNotification, type BellNotificationInput } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';

/**
 * Consumers de Connectors -> notificaciones in-app. Fase 1B del plan de
 * notificaciones dinamicas: ambos eventos ya llevan `CreatedByUserId`
 * (quien conecto la cuenta de correo), pero antes de esta fase no tenian
 * NINGUN consumer en ningun servicio — quien conecto la cuenta nunca se
 * enteraba de que necesitaba reconectarla.
 */
export function bindConnectorsConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    notifications: NotificationRepository;
    emitter: RealtimeEmitter;
    inAppSource: 'Communication' | 'Notification';
  },
): void {
  if (deps.inAppSource === 'Notification') return;
  register('connectors.oauth.refresh_failed.v1', (env) => oauthRefreshFailedHandler(env, deps));
  register('connectors.watch.expired.v1', (env) => watchExpiredHandler(env, deps));
}

async function oauthRefreshFailedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const accountId = getString(env.payload, 'accountId') ?? getString(env.payload, 'AccountId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const providerCode = getString(env.payload, 'providerCode') ?? getString(env.payload, 'ProviderCode') ?? '';
  if (!accountId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'connectors.oauth.refresh_failed',
    priority: 'High',
    title: `Reconnect ${providerCode}`,
    body: `We could not renew access to your ${providerCode} mailbox. Reconnect it to keep receiving mail.`,
    metadata: { accountId, providerCode },
    category: 'Work',
    actorUserId: null,
    // El refresh del OAuth corre en background sin usuario — es el scheduler/token manager.
    actorType: 'System',
    resourceType: 'mailbox',
    resourceId: accountId,
    ...(providerCode ? { resourceContext: { providerCode } } : {}),
    action: 'reconnect',
    occurredAtUtc: envOccurredAt(env),
  }, deps);
}

async function watchExpiredHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const accountId = getString(env.payload, 'accountId') ?? getString(env.payload, 'AccountId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const providerCode = getString(env.payload, 'providerCode') ?? getString(env.payload, 'ProviderCode') ?? '';
  if (!accountId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'connectors.watch.expired',
    priority: 'High',
    title: 'Mailbox connection expired',
    body: `We stopped receiving new mail from your ${providerCode} mailbox. Reconnect it to resume syncing.`,
    metadata: { accountId, providerCode },
    category: 'Work',
    actorUserId: null,
    actorType: 'System',
    resourceType: 'mailbox',
    resourceId: accountId,
    ...(providerCode ? { resourceContext: { providerCode } } : {}),
    action: 'reconnect',
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

/**
 * `OccurredOn` del envelope .NET: hora REAL del hecho (p.ej. cuándo el watch
 * expiró en Google/Microsoft), distinta de `CreatedAtUtc` del repo que es la
 * hora de recepción.
 */
function envOccurredAt(env: IncomingEnvelope): Date {
  const raw = getString(env.payload, 'occurredOn') ?? getString(env.payload, 'OccurredOn');
  if (!raw) return new Date();
  const parsed = new Date(raw);
  return Number.isFinite(parsed.getTime()) ? parsed : new Date();
}
