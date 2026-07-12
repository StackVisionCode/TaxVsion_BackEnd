import { pushNotification, type PushNotificationCommand } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';
import { randomUUID } from 'node:crypto';

/**
 * Consumers de Signature -> in-app notifications. Cada handler:
 *   1. Extrae del envelope los IDs relevantes.
 *   2. Determina destinatario (SignerId, HostUserId, etc.).
 *   3. Compone `pushNotification` con kind estable + metadata deep-link.
 *   4. Si created=true, emite `notification.received` al room del user.
 *
 * Cierra pendiente README §29.19: canal push app para SignerVerificationChallengeIssued.
 * Cierra README §29.10: notifs in-app para Signature ciclo de vida.
 */
export function bindSignatureConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): void {
  register('signature.signer.invited.v1', (env) =>
    signerInvitedHandler(env, deps),
  );
  register('signature.document.signed.v1', (env) =>
    documentSignedHandler(env, deps),
  );
  register('signature.request.completed.v1', (env) =>
    requestCompletedHandler(env, deps),
  );
  register('signature.request.canceled.v1', (env) =>
    requestCanceledHandler(env, deps),
  );
  register('signature.request.reminder_due.v1', (env) =>
    reminderDueHandler(env, deps),
  );
  register('signature.request.sealed.v1', (env) =>
    requestSealedHandler(env, deps),
  );
  register('signature.signer.verification.challenge_issued.v1', (env) =>
    pushChallengeHandler(env, deps),
  );
}

async function signerInvitedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const signerId = getString(env.payload, 'signerId') ?? getString(env.payload, 'SignerId');
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  if (!signerId || !requestId) return;
  await push(env, {
    userId: signerId,
    kind: 'signature.signer.invited',
    priority: 'Normal',
    title: 'Nuevo documento para firmar',
    body: 'Se te invito a firmar un documento.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

async function documentSignedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'requestCreatedByUserId') ?? getString(env.payload, 'RequestCreatedByUserId');
  if (!requestId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.document.signed',
    priority: 'Normal',
    title: 'Firma recibida',
    body: 'Un firmante completo su firma en un documento.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

async function requestCompletedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.request.completed',
    priority: 'High',
    title: 'Solicitud de firma completada',
    body: 'Todos los firmantes han firmado el documento.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

async function requestCanceledHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.request.canceled',
    priority: 'Normal',
    title: 'Solicitud de firma cancelada',
    body: 'La solicitud de firma fue cancelada.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

async function reminderDueHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const signerId = getString(env.payload, 'signerId') ?? getString(env.payload, 'SignerId');
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  if (!signerId || !requestId) return;
  await push(env, {
    userId: signerId,
    kind: 'signature.reminder_due',
    priority: 'High',
    title: 'Recordatorio: firma pendiente',
    body: 'Tienes un documento por firmar que esta por vencer.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

async function requestSealedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.request.sealed',
    priority: 'Normal',
    title: 'Documento sellado',
    body: 'El PDF fue sellado con PAdES-B y esta disponible.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

/**
 * Push app-based challenge — canal `App` de Signature. Cierra pendiente
 * README §29.19 explicito.
 */
async function pushChallengeHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const method = getString(env.payload, 'method') ?? getString(env.payload, 'Method');
  if (method !== 'App') return; // solo procesamos App-based; SmsOtp/EmailOtp van por Notification service.
  const signerId = getString(env.payload, 'signerId') ?? getString(env.payload, 'SignerId');
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  if (!signerId || !requestId) return;
  await push(env, {
    userId: signerId,
    kind: 'signature.push_challenge',
    priority: 'Urgent',
    title: 'Verificacion requerida',
    body: 'Aprueba el reto en la app para continuar con la firma.',
    metadata: { signatureRequestId: requestId, method: 'App' },
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
