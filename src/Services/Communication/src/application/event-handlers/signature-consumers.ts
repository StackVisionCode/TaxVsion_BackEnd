import { pushAndEmitNotification, type BellNotificationInput } from '../use-cases/push-notification.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import { randomUUID } from 'node:crypto';

// Consumers de firma -> notificaciones in-app. Resuelven destinatario,
// componen el payload canonico (resourceType/resourceId/titleKey/action/...)
// y, cuando el evento cambia la fila de la lista del CRM, emiten tambien
// un ping por-tenant para refrescar la tabla sin recargar.
type SignatureConsumerDeps = {
  notifications: NotificationRepository;
  emitter: RealtimeEmitter;
  customerPortalAccounts: CustomerPortalAccountRepository;
};

// Eventos de firma que cambian el estado visible en la lista del CRM.
// Los eventos informativos no estan aqui, por eso no emiten el ping.
const LIST_CHANGING_EVENTS = new Set<string>([
  'signature.document.signed.v1',
  'signature.signer.rejected.v1',
  'signature.request.completed.v1',
  'signature.request.canceled.v1',
  'signature.request.expired.v1',
  'signature.request.ready_for_sending.v1',
  'signature.request.expiration_extended.v1',
  'signature.preparer.signed.v1',
]);

const SIGNATURE_REQUEST_CHANGED_EVENT = 'signature.request.changed';

const SIGNATURES_CATEGORY = 'Signatures';
const RESOURCE_SIGNATURE_REQUEST = 'signature_request';

export function bindSignatureConsumers(
  registerRaw: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: SignatureConsumerDeps,
): void {
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>): void => {
    registerRaw(eventType, async (env) => {
      await handler(env);
      if (LIST_CHANGING_EVENTS.has(eventType)) {
        emitSignatureListChanged(env, deps.emitter);
      }
    });
  };

  register('signature.document.signed.v1', (env) => documentSignedHandler(env, deps));
  register('signature.request.completed.v1', (env) => requestCompletedHandler(env, deps));
  register('signature.request.canceled.v1', (env) => requestCanceledHandler(env, deps));
  register('signature.signer.rejected.v1', (env) => signerRejectedHandler(env, deps));
  register('signature.signer.verification.failed.v1', (env) => verificationFailedHandler(env, deps));
  register('signature.request.sealing_failed.v1', (env) => sealingFailedHandler(env, deps));
  register('signature.request.expiration_extended.v1', listOnlyHandler);
  register('signature.request.ready_for_sending.v1', listOnlyHandler);
  register('signature.signer.pin_failed.v1', (env) => pinFailedHandler(env, deps));
  register('signature.preparer.signed.v1', listOnlyHandler);
  register('signature.request.expired.v1', (env) => requestExpiredHandler(env, deps));
}

// La misma firma del documento genera dos filas con audiencias y copys distintos:
// el preparador (CreatedByUserId, siempre existe) y el firmante-cliente del portal
// (opcional: solo si MappedCustomerId tiene cuenta de portal activa). Los kinds
// son distintos para que el front resuelva copy e icono por audiencia; el deep link
// comun es el mismo (signature_request + id).
async function documentSignedHandler(env: IncomingEnvelope, deps: SignatureConsumerDeps): Promise<void> {
  const requestId =
    getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;

  await push(
    env,
    {
      userId: createdBy,
      kind: 'signature.document.signed',
      priority: 'Normal',
      title: 'A signer completed a document',
      body: 'A signer finished signing a document on your request.',
      resourceType: RESOURCE_SIGNATURE_REQUEST,
      resourceId: requestId,
      category: SIGNATURES_CATEGORY,
      action: 'review',
      titleKey: 'notif.signature.document.signed.title',
      bodyKey: 'notif.signature.document.signed.body',
      params: { signatureRequestId: requestId },
    },
    deps,
  );

  const mappedCustomerId =
    getString(env.payload, 'mappedCustomerId') ?? getString(env.payload, 'MappedCustomerId');
  if (!mappedCustomerId) return;
  const portalAccount = await deps.customerPortalAccounts.findActiveByCustomerId(mappedCustomerId);
  if (!portalAccount) return;

  await push(
    env,
    {
      userId: portalAccount.userId,
      kind: 'signature.document.signed.customer_signer',
      priority: 'Normal',
      title: 'Your signature was recorded',
      body: 'Your signature on the document has been recorded.',
      resourceType: RESOURCE_SIGNATURE_REQUEST,
      resourceId: requestId,
      category: SIGNATURES_CATEGORY,
      action: 'open',
      titleKey: 'notif.signature.document.signed.customer_signer.title',
      bodyKey: 'notif.signature.document.signed.customer_signer.body',
      params: { signatureRequestId: requestId },
    },
    deps,
  );
}

async function requestCompletedHandler(
  env: IncomingEnvelope,
  deps: SignatureConsumerDeps,
): Promise<void> {
  const requestId =
    getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  await push(
    env,
    {
      userId: createdBy,
      kind: 'signature.request.completed',
      priority: 'High',
      title: 'Signature request completed',
      body: 'All signers have signed the document.',
      resourceType: RESOURCE_SIGNATURE_REQUEST,
      resourceId: requestId,
      category: SIGNATURES_CATEGORY,
      action: 'review',
      titleKey: 'notif.signature.request.completed.title',
      bodyKey: 'notif.signature.request.completed.body',
      params: { signatureRequestId: requestId },
    },
    deps,
  );
}

async function requestCanceledHandler(
  env: IncomingEnvelope,
  deps: SignatureConsumerDeps,
): Promise<void> {
  const requestId =
    getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const canceledBy = getString(env.payload, 'canceledByUserId') ?? getString(env.payload, 'CanceledByUserId');
  if (!requestId || !createdBy || !canceledBy || createdBy === canceledBy) return;
  await push(
    env,
    {
      userId: createdBy,
      kind: 'signature.request.canceled_by_another',
      priority: 'Normal',
      title: 'Signature request canceled',
      body: 'Your signature request was canceled.',
      resourceType: RESOURCE_SIGNATURE_REQUEST,
      resourceId: requestId,
      category: SIGNATURES_CATEGORY,
      action: 'open',
      titleKey: 'notif.signature.request.canceled_by_another.title',
      bodyKey: 'notif.signature.request.canceled_by_another.body',
      params: { signatureRequestId: requestId, canceledByUserId: canceledBy },
      // Único handler del archivo donde el actor es un usuario del tenant
      // identificable (quién canceló la request). En el resto, el "actor" es
      // el firmante externo o el sistema (expired/sealing_failed) y queda null.
      actorUserId: canceledBy,
    },
    deps,
  );
}

async function signerRejectedHandler(
  env: IncomingEnvelope,
  deps: SignatureConsumerDeps,
): Promise<void> {
  const requestId =
    getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  const reason = getString(env.payload, 'reason') ?? getString(env.payload, 'Reason');
  await push(
    env,
    {
      userId: createdBy,
      kind: 'signature.signer.rejected',
      priority: 'High',
      title: 'A signer declined to sign',
      body: reason ?? 'A signer declined to sign the document.',
      resourceType: RESOURCE_SIGNATURE_REQUEST,
      resourceId: requestId,
      category: SIGNATURES_CATEGORY,
      action: 'review',
      titleKey: 'notif.signature.signer.rejected.title',
      bodyKey: 'notif.signature.signer.rejected.body',
      params: reason ? { signatureRequestId: requestId, reason } : { signatureRequestId: requestId },
    },
    deps,
  );
}

// Solo alertamos cuando el firmante queda bloqueado (LockedUntilUtc), para no
// generar una notificacion por cada intento fallido.
async function verificationFailedHandler(
  env: IncomingEnvelope,
  deps: SignatureConsumerDeps,
): Promise<void> {
  const requestId =
    getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const lockedUntil = getString(env.payload, 'lockedUntilUtc') ?? getString(env.payload, 'LockedUntilUtc');
  if (!requestId || !createdBy || !lockedUntil) return;
  await push(
    env,
    {
      userId: createdBy,
      kind: 'signature.signer.verification_failed',
      priority: 'High',
      title: 'Signer verification locked',
      body: 'A signer exceeded the verification attempts limit and is temporarily locked.',
      resourceType: RESOURCE_SIGNATURE_REQUEST,
      resourceId: requestId,
      category: SIGNATURES_CATEGORY,
      action: 'review',
      titleKey: 'notif.signature.signer.verification_failed.title',
      bodyKey: 'notif.signature.signer.verification_failed.body',
      params: { signatureRequestId: requestId },
    },
    deps,
  );
}

async function sealingFailedHandler(
  env: IncomingEnvelope,
  deps: SignatureConsumerDeps,
): Promise<void> {
  const requestId =
    getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  const reason = getString(env.payload, 'reason') ?? getString(env.payload, 'Reason');
  await push(
    env,
    {
      userId: createdBy,
      kind: 'signature.request.sealing_failed',
      priority: 'Urgent',
      title: 'Document sealing failed',
      body: reason ?? 'PAdES-B sealing failed after retries.',
      resourceType: RESOURCE_SIGNATURE_REQUEST,
      resourceId: requestId,
      category: SIGNATURES_CATEGORY,
      action: 'review',
      titleKey: 'notif.signature.request.sealing_failed.title',
      bodyKey: 'notif.signature.request.sealing_failed.body',
      params: reason ? { signatureRequestId: requestId, reason } : { signatureRequestId: requestId },
    },
    deps,
  );
}

// Mismo criterio que verificationFailedHandler: solo cuando queda bloqueado.
async function pinFailedHandler(
  env: IncomingEnvelope,
  deps: SignatureConsumerDeps,
): Promise<void> {
  const requestId =
    getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  const lockedUntil = getString(env.payload, 'lockedUntilUtc') ?? getString(env.payload, 'LockedUntilUtc');
  if (!requestId || !createdBy || !lockedUntil) return;
  await push(
    env,
    {
      userId: createdBy,
      kind: 'signature.signer.pin_failed',
      priority: 'High',
      title: 'Signer locked by PIN attempts',
      body: 'A signer exceeded the PIN attempts limit and is temporarily locked.',
      resourceType: RESOURCE_SIGNATURE_REQUEST,
      resourceId: requestId,
      category: SIGNATURES_CATEGORY,
      action: 'review',
      titleKey: 'notif.signature.signer.pin_failed.title',
      bodyKey: 'notif.signature.signer.pin_failed.body',
      params: { signatureRequestId: requestId },
    },
    deps,
  );
}

function listOnlyHandler(_env: IncomingEnvelope): Promise<void> {
  return Promise.resolve();
}

async function requestExpiredHandler(
  env: IncomingEnvelope,
  deps: SignatureConsumerDeps,
): Promise<void> {
  const requestId =
    getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  const createdBy = getString(env.payload, 'createdByUserId') ?? getString(env.payload, 'CreatedByUserId');
  if (!requestId || !createdBy) return;
  await push(
    env,
    {
      userId: createdBy,
      kind: 'signature.request.expired',
      priority: 'High',
      title: 'Signature request expired',
      body: 'The signature request expired before all signers completed.',
      resourceType: RESOURCE_SIGNATURE_REQUEST,
      resourceId: requestId,
      category: SIGNATURES_CATEGORY,
      action: 'review',
      titleKey: 'notif.signature.request.expired.title',
      bodyKey: 'notif.signature.request.expired.body',
      params: { signatureRequestId: requestId },
    },
    deps,
  );
}

async function push(
  env: IncomingEnvelope,
  input: BellNotificationInput,
  deps: SignatureConsumerDeps,
): Promise<void> {
  // Default `actorType` para firma: cuando el caller no lo setea, el signer que
  // firmó/rechazó/pinchó es externo al tenant (no tiene cuenta Auth); solo
  // `canceled_by_another` tiene un TenantEmployee identificable (lo setea arriba).
  // Los eventos de sistema (expired, sealing_failed) son automáticos.
  const isSystemEvent = /expired|sealing_failed/.test(input.kind);
  const defaultActorType = isSystemEvent ? 'System' : 'External';
  await pushAndEmitNotification(
    env,
    {
      ...input,
      occurredAtUtc: input.occurredAtUtc ?? envOccurredAt(env),
      actorType: input.actorType ?? defaultActorType,
    },
    deps,
  );
}

/**
 * `OccurredOn` del envelope .NET: hora REAL del hecho (p.ej. cuándo se firmó),
 * distinta de `CreatedAtUtc` del repo que es la hora de recepción.
 */
function envOccurredAt(env: IncomingEnvelope): Date {
  const raw = getString(env.payload, 'occurredOn') ?? getString(env.payload, 'OccurredOn');
  if (!raw) return new Date();
  const parsed = new Date(raw);
  return Number.isFinite(parsed.getTime()) ? parsed : new Date();
}

// Ping por-tenant: el CRM refresca su lista con un GET; aqui solo viaja el id.
function emitSignatureListChanged(env: IncomingEnvelope, emitter: RealtimeEmitter): void {
  const requestId =
    getString(env.payload, 'signatureRequestId') ?? getString(env.payload, 'SignatureRequestId');
  if (!requestId) return;
  emitter.emitToTenantStaff({
    tenantId: env.tenantId,
    event: SIGNATURE_REQUEST_CHANGED_EVENT,
    envelope: {
      eventId: randomUUID(),
      correlationId: env.correlationId ?? '',
      emittedAtUtc: new Date().toISOString(),
      payload: { signatureRequestId: requestId },
    },
  });
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}
