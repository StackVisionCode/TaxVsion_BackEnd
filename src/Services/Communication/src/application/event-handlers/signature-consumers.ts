import { pushNotification, type PushNotificationCommand } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { NotificationActionMappingRepository } from '../ports/notification-action-mapping-repository.js';
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
 *
 * Fase 6 (notificaciones dinamicas): `documentSignedHandler` es el unico migrado a
 * doble audiencia (Preparer + CustomerSigner) resuelta via `NotificationActionMapping`
 * — el resto de los handlers de este archivo ya tenian un destinatario explicito real
 * y quedan sin tocar, por alcance explicito del plan.
 */
type SignatureConsumerDeps = {
  notifications: NotificationRepository;
  emitter: RealtimeEmitter;
  customerPortalAccounts: CustomerPortalAccountRepository;
  actionMappings: NotificationActionMappingRepository;
};

export function bindSignatureConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: SignatureConsumerDeps,
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
  register('signature.signer.rejected.v1', (env) =>
    signerRejectedHandler(env, deps),
  );
  register('signature.signer.verification.failed.v1', (env) =>
    verificationFailedHandler(env, deps),
  );
  register('signature.request.sealing_failed.v1', (env) =>
    sealingFailedHandler(env, deps),
  );
  register('signature.request.expiration_extended.v1', (env) =>
    expirationExtendedHandler(env, deps),
  );
  register('signature.request.ready_for_sending.v1', (env) =>
    readyForSendingHandler(env, deps),
  );
  register('signature.signer.pin_failed.v1', (env) =>
    pinFailedHandler(env, deps),
  );
  register('signature.preparer.signed.v1', (env) =>
    preparerSignedHandler(env, deps),
  );
  register('signature.request.expired.v1', (env) =>
    requestExpiredHandler(env, deps),
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

/**
 * Fase 6: la misma "cosa que paso" (un firmante firmo) genera hasta DOS notificaciones
 * independientes, cada una con su propia audiencia y su propia accion — nunca una sola
 * notificacion "compartida". El preparador (createdBy) siempre existe y ya se resolvia
 * antes de esta fase; el cliente-firmante (CustomerSigner) es nuevo y condicional: solo
 * si el Signer esta vinculado a un Customer registrado (MappedCustomerId) Y ese Customer
 * tiene una cuenta de portal activa hoy. Un firmante externo (el caso mas comun) no
 * genera ninguna fila nueva — sigue con su email de siempre, sin error ni intento fallido.
 */
async function documentSignedHandler(env: IncomingEnvelope, deps: SignatureConsumerDeps): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;

  const preparerAction = await resolveAction(env.eventType, 'Preparer', deps.actionMappings, {
    signatureRequestId: requestId,
  });
  await push(
    env,
    {
      userId: createdBy,
      kind: 'signature.document.signed',
      priority: 'Normal',
      title: 'Firma recibida',
      body: 'Un firmante completo su firma en un documento.',
      metadata: { signatureRequestId: requestId, ...preparerAction },
    },
    deps,
  );

  const mappedCustomerId = getString(env.payload, 'mappedCustomerId') ?? getString(env.payload, 'MappedCustomerId');
  if (!mappedCustomerId) return;
  const portalAccount = await deps.customerPortalAccounts.findActiveByCustomerId(mappedCustomerId);
  if (!portalAccount) return;

  const customerAction = await resolveAction(env.eventType, 'CustomerSigner', deps.actionMappings, {
    signatureRequestId: requestId,
  });
  await push(
    env,
    {
      userId: portalAccount.userId,
      kind: 'signature.document.signed',
      priority: 'Normal',
      title: 'Documento firmado',
      body: 'Tu firma en el documento fue registrada correctamente.',
      metadata: { signatureRequestId: requestId, ...customerAction },
    },
    deps,
  );
}

/**
 * Resuelve la accion (deep-link o ninguna) para una combinacion (evento, audiencia) via
 * `NotificationActionMapping`. Sin fila (mapping no seedeado todavia) o ActionType=None:
 * la notificacion se crea igual, solo que sin accion asociada (informativa).
 */
async function resolveAction(
  eventKey: string,
  audienceRole: string,
  mappings: NotificationActionMappingRepository,
  placeholders: Readonly<Record<string, string>>,
): Promise<{ actionType: 'DeepLink' | 'None'; actionUrl?: string }> {
  const mapping = await mappings.findByEventKeyAndAudienceRole(eventKey, audienceRole);
  if (!mapping || mapping.actionType === 'None' || !mapping.urlTemplate) {
    return { actionType: 'None' };
  }
  const actionUrl = mapping.urlTemplate.replace(/\{(\w+)\}/g, (match, key: string) => placeholders[key] ?? match);
  return { actionType: 'DeepLink', actionUrl };
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

async function signerRejectedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  const reason = getString(env.payload, 'reason') ?? getString(env.payload, 'Reason');
  await push(env, {
    userId: createdBy,
    kind: 'signature.signer.rejected',
    priority: 'High',
    title: 'Firmante rechazo firmar',
    body: reason
      ? `Un firmante rechazo firmar el documento: ${reason}`
      : 'Un firmante rechazo firmar el documento.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

/**
 * Solo alertamos al preparador cuando el firmante queda bloqueado
 * (`LockedUntilUtc` presente), no en cada intento fallido individual —
 * evita spamear una notificación por cada dígito equivocado de un PIN.
 */
async function verificationFailedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const lockedUntil = getString(env.payload, 'lockedUntilUtc') ?? getString(env.payload, 'LockedUntilUtc');
  if (!requestId || !createdBy || !lockedUntil) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.signer.verification_failed',
    priority: 'High',
    title: 'Verificacion de firmante bloqueada',
    body: 'Un firmante supero el limite de intentos de verificacion y quedo bloqueado temporalmente.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

/**
 * Fase 8 (cierre de la auditoría Fase 1B) — 5 consumers nuevos, todos con destinatario
 * explícito (CreatedByUserId, el preparador), mismo patrón mecánico que
 * signerRejectedHandler/verificationFailedHandler. No requieren fan-out ni resolución de
 * audiencia por permiso: cada evento ya trae un único destinatario obvio.
 */
async function sealingFailedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  const reason = getString(env.payload, 'reason') ?? getString(env.payload, 'Reason');
  await push(env, {
    userId: createdBy,
    kind: 'signature.request.sealing_failed',
    priority: 'Urgent',
    title: 'Sellado del documento fallo',
    body: reason
      ? `El sellado PAdES-B fallo tras agotar los reintentos: ${reason}`
      : 'El sellado PAdES-B fallo tras agotar los reintentos.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

async function expirationExtendedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.request.expiration_extended',
    priority: 'Normal',
    title: 'Vencimiento de la solicitud extendido',
    body: 'Se extendio el vencimiento de una solicitud de firma.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

async function readyForSendingHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.request.ready_for_sending',
    priority: 'Normal',
    title: 'Solicitud lista para enviar',
    body: 'El documento ya esta disponible y la solicitud puede enviarse a los firmantes.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

/**
 * Solo alertamos al preparador cuando el firmante queda bloqueado (`LockedUntilUtc`
 * presente) — mismo criterio que verificationFailedHandler, para no spamear una
 * notificación por cada PIN equivocado.
 */
async function pinFailedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const lockedUntil = getString(env.payload, 'lockedUntilUtc') ?? getString(env.payload, 'LockedUntilUtc');
  if (!requestId || !createdBy || !lockedUntil) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.signer.pin_failed',
    priority: 'High',
    title: 'Firmante bloqueado por intentos de PIN',
    body: 'Un firmante supero el limite de intentos de PIN y quedo bloqueado temporalmente.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

async function preparerSignedHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const preparerName =
    getString(env.payload, 'preparerDisplayName') ?? getString(env.payload, 'PreparerDisplayName');
  if (!requestId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.preparer.signed',
    priority: 'Normal',
    title: 'Firma interna del preparador registrada',
    body: preparerName ? `${preparerName} firmo internamente la solicitud.` : 'Se registro una firma interna del preparador.',
    metadata: { signatureRequestId: requestId },
  }, deps);
}

/**
 * Avisa al preparador (no a los firmantes, esos ya reciben su email desde Notification/.NET
 * vía `SignatureRequestExpiredConsumer`) que su solicitud venció sin completarse.
 */
async function requestExpiredHandler(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const requestId = getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  await push(env, {
    userId: createdBy,
    kind: 'signature.request.expired',
    priority: 'High',
    title: 'Solicitud de firma vencida',
    body: 'La solicitud de firma vencio sin que todos los firmantes completaran.',
    metadata: { signatureRequestId: requestId },
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
