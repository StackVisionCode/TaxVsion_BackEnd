import { describe, expect, it, vi } from 'vitest';
import { bindSignatureConsumers } from '../../src/application/event-handlers/signature-consumers.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { CustomerPortalAccountRepository } from '../../src/application/ports/customer-portal-account-repository.js';

// Contrato con Signature (.NET): los payloads usan los nombres exactos de los
// records en BuildingBlocks/Messaging/SignatureIntegrationEvents/. Si el nombre
// se desalinea, los handlers caen al guard `if (!requestId || !createdBy) return`
// y la notificacion no se crea. Este test existe para fallar en CI, no en vivo.

function setup() {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(eventType, handler);
  };
  const notifications: NotificationRepository = {
    createIfMissing: vi.fn().mockResolvedValue(true),
    findById: vi.fn(),
    markRead: vi.fn().mockResolvedValue(true),
    dismiss: vi.fn().mockResolvedValue(true),
    markAllRead: vi.fn().mockResolvedValue({ markedCount: 0, unreadCount: 0 }),
    listForUser: vi.fn(),
    countUnread: vi.fn().mockResolvedValue(0),
    markUnread: vi.fn().mockResolvedValue(true),
    markManyRead: vi.fn().mockResolvedValue({ markedCount: 0, unreadCount: 0 }),
    markManyUnread: vi.fn().mockResolvedValue({ unmarkedCount: 0, unreadCount: 0 }),
    dismissMany: vi.fn().mockResolvedValue({ dismissedCount: 0, unreadCount: 0 }),
    countForUser: vi.fn().mockResolvedValue(0),
    listSinceForUser: vi.fn().mockResolvedValue([]),
  };
  const emitter: RealtimeEmitter = {
    emitToUser: vi.fn(),
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToTenant: vi.fn(),
    emitToTenantStaff: vi.fn(),
    emitToTenantMembers: vi.fn(),
    emitBroadcast: vi.fn(),
  } as unknown as RealtimeEmitter;

  const customerPortalAccounts: CustomerPortalAccountRepository = {
    upsert: vi.fn(),
    markInactiveByUserId: vi.fn(),
    markActiveByUserId: vi.fn(),
    findActiveByCustomerId: vi.fn().mockResolvedValue(null),
    findActiveByCustomerIds: vi.fn().mockResolvedValue([]),
    findActiveByUserId: vi.fn().mockResolvedValue(null),
  };

  bindSignatureConsumers(register, { notifications, emitter, customerPortalAccounts });
  return { handlers, notifications, emitter, customerPortalAccounts };
}

function envelope(eventType: string, payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType,
    tenantId: 'tenant-1',
    correlationId: 'corr-1',
    occurredOnUtc: new Date().toISOString(),
    payload,
  };
}

describe('bindSignatureConsumers — contrato de campos con Signature (.NET)', () => {
  it('document.signed.v1 crea la notificacion para CreatedByUserId con resourceType canonico', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('signature.document.signed.v1')!(
      envelope('signature.document.signed.v1', {
        SignatureRequestId: 'req-1',
        SignerId: 'signer-1',
        CreatedByUserId: 'preparer-1',
        SignedAtUtc: new Date().toISOString(),
        TotalSignersCount: 2,
        SignedSignersCount: 1,
        IsRequestCompleted: false,
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    const snapshot = vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.toSnapshot();
    expect(snapshot.userId).toBe('preparer-1');
    expect(snapshot.kind).toBe('signature.document.signed');
    expect(snapshot.resourceType).toBe('signature_request');
    expect(snapshot.resourceId).toBe('req-1');
    expect(snapshot.action).toBe('review');
    expect(snapshot.titleKey).toBe('notif.signature.document.signed.title');
    expect(snapshot.category).toBe('Signatures');
  });

  it('document.signed.v1 con MappedCustomerId sin cuenta de portal activa NO genera una segunda notificacion', async () => {
    const { handlers, notifications, customerPortalAccounts } = setup();
    vi.mocked(customerPortalAccounts.findActiveByCustomerId).mockResolvedValue(null);

    await handlers.get('signature.document.signed.v1')!(
      envelope('signature.document.signed.v1', {
        SignatureRequestId: 'req-1b',
        SignerId: 'signer-1b',
        CreatedByUserId: 'preparer-1b',
        MappedCustomerId: 'customer-without-portal',
        SignedAtUtc: new Date().toISOString(),
        TotalSignersCount: 1,
        SignedSignersCount: 1,
        IsRequestCompleted: true,
      }),
    );

    expect(customerPortalAccounts.findActiveByCustomerId).toHaveBeenCalledWith('customer-without-portal');
    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
  });

  it('document.signed.v1 con cuenta de portal activa notifica a AMBAS audiencias con kinds distintos', async () => {
    const { handlers, notifications, customerPortalAccounts } = setup();
    vi.mocked(customerPortalAccounts.findActiveByCustomerId).mockResolvedValue({
      customerId: 'customer-1',
      tenantId: 'tenant-1',
      userId: 'customer-portal-user-1',
      isActive: true,
    });

    await handlers.get('signature.document.signed.v1')!(
      envelope('signature.document.signed.v1', {
        SignatureRequestId: 'req-1c',
        SignerId: 'signer-1c',
        CreatedByUserId: 'preparer-1c',
        MappedCustomerId: 'customer-1',
        SignedAtUtc: new Date().toISOString(),
        TotalSignersCount: 1,
        SignedSignersCount: 1,
        IsRequestCompleted: true,
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(2);
    const calls = vi.mocked(notifications.createIfMissing).mock.calls;
    const preparer = calls
      .map((c) => c[0]!.toSnapshot())
      .find((s) => s.userId === 'preparer-1c')!;
    const customer = calls
      .map((c) => c[0]!.toSnapshot())
      .find((s) => s.userId === 'customer-portal-user-1')!;
    expect(preparer.kind).toBe('signature.document.signed');
    expect(preparer.action).toBe('review');
    expect(customer.kind).toBe('signature.document.signed.customer_signer');
    expect(customer.action).toBe('open');
    expect(customer.titleKey).toBe('notif.signature.document.signed.customer_signer.title');
    expect(customer.resourceId).toBe('req-1c');
  });

  it('request.completed.v1 crea la notificacion para CreatedByUserId', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('signature.request.completed.v1')!(
      envelope('signature.request.completed.v1', {
        SignatureRequestId: 'req-2',
        CreatedByUserId: 'preparer-2',
        CompletedAtUtc: new Date().toISOString(),
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.toSnapshot().userId).toBe('preparer-2');
  });

  it('request.canceled.v1 notifica al creador cuando otra persona cancela', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('signature.request.canceled.v1')!(
      envelope('signature.request.canceled.v1', {
        SignatureRequestId: 'req-3',
        CreatedByUserId: 'preparer-3',
        CanceledByUserId: 'admin-who-canceled',
        CanceledAtUtc: new Date().toISOString(),
        PendingSignerIds: [],
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.toSnapshot().userId).toBe('preparer-3');
  });

  it('request.canceled.v1 no notifica al creador cuando cancela su propia solicitud', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('signature.request.canceled.v1')!(
      envelope('signature.request.canceled.v1', {
        SignatureRequestId: 'req-self-canceled',
        CreatedByUserId: 'preparer-3',
        CanceledByUserId: 'preparer-3',
        CanceledAtUtc: new Date().toISOString(),
        PendingSignerIds: [],
      }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });

  it('eventos de mantenimiento refrescan la lista sin crear campana', async () => {
    const { handlers, notifications, emitter } = setup();

    for (const eventType of [
      'signature.request.expiration_extended.v1',
      'signature.request.ready_for_sending.v1',
      'signature.preparer.signed.v1',
    ]) {
      await handlers.get(eventType)!(envelope(eventType, { SignatureRequestId: 'req-list-only' }));
    }

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
    expect(emitter.emitToTenantStaff).toHaveBeenCalledTimes(3);
  });

  it('no registra handlers de bandeja para ids de firmante ni para el sellado redundante', () => {
    const { handlers } = setup();

    expect(handlers.has('signature.signer.invited.v1')).toBe(false);
    expect(handlers.has('signature.request.reminder_due.v1')).toBe(false);
    expect(handlers.has('signature.signer.verification.challenge_issued.v1')).toBe(false);
    expect(handlers.has('signature.request.sealed.v1')).toBe(false);
  });

  it('no crea ninguna notificacion si CreatedByUserId falta (regresion del bug original)', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('signature.request.completed.v1')!(
      envelope('signature.request.completed.v1', {
        SignatureRequestId: 'req-5',
        CompletedAtUtc: new Date().toISOString(),
      }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });

  it('signer.rejected.v1 crea la notificacion para CreatedByUserId con reason en params', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('signature.signer.rejected.v1')!(
      envelope('signature.signer.rejected.v1', {
        SignatureRequestId: 'req-6',
        CreatedByUserId: 'preparer-6',
        SignerId: 'signer-6',
        RejectedAtUtc: new Date().toISOString(),
        RevocationEpoch: 1,
        Reason: 'No reconozco este documento',
        PendingSignerIds: [],
        PendingSigners: [],
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    const snapshot = vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.toSnapshot();
    expect(snapshot.userId).toBe('preparer-6');
    expect(snapshot.params?.['reason']).toBe('No reconozco este documento');
  });

  it('signer.verification.failed.v1 crea la notificacion para CreatedByUserId solo cuando LockedUntilUtc esta presente', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('signature.signer.verification.failed.v1')!(
      envelope('signature.signer.verification.failed.v1', {
        SignatureRequestId: 'req-7',
        CreatedByUserId: 'preparer-7',
        SignerId: 'signer-7',
        Method: 'Pin',
        AttemptedAtUtc: new Date().toISOString(),
        FailedAttempts: 5,
        LockedUntilUtc: new Date(Date.now() + 60_000).toISOString(),
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.toSnapshot().userId).toBe('preparer-7');
  });

  it('signer.verification.failed.v1 NO notifica en un intento fallido individual (sin LockedUntilUtc)', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('signature.signer.verification.failed.v1')!(
      envelope('signature.signer.verification.failed.v1', {
        SignatureRequestId: 'req-8',
        CreatedByUserId: 'preparer-8',
        SignerId: 'signer-8',
        Method: 'Pin',
        AttemptedAtUtc: new Date().toISOString(),
        FailedAttempts: 1,
      }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });
});
