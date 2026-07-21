import { randomUUID } from 'node:crypto';
import { pushNotification, type PushNotificationCommand } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';

/**
 * Consumers de Connectors -> notificaciones in-app. Fase 1B del plan de
 * notificaciones dinamicas: ambos eventos ya llevan `CreatedByUserId`
 * (quien conecto la cuenta de correo), pero antes de esta fase no tenian
 * NINGUN consumer en ningun servicio — quien conecto la cuenta nunca se
 * enteraba de que necesitaba reconectarla.
 */
export function bindConnectorsConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): void {
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
    title: 'Tu cuenta de correo necesita reconexion',
    body: `No pudimos renovar el acceso a tu cuenta ${providerCode}. Reconectala para seguir recibiendo correo.`,
    metadata: { accountId, providerCode },
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
    title: 'Tu cuenta de correo necesita reconexion',
    body: `Dejamos de recibir correo nuevo de tu cuenta ${providerCode}. Reconectala para reanudar la sincronizacion.`,
    metadata: { accountId, providerCode },
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
