import { describe, expect, it, vi } from 'vitest';
import { bindSignatureConsumers } from '../../src/application/event-handlers/signature-consumers.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { CustomerPortalAccountRepository } from '../../src/application/ports/customer-portal-account-repository.js';
import type { NotificationActionMappingRepository } from '../../src/application/ports/notification-action-mapping-repository.js';

/**
 * Test de contrato (regla de la Fase 0 del plan de notificaciones): los payloads de
 * abajo usan los nombres de campo EXACTOS que .NET serializa hoy (PascalCase, sin
 * naming policy configurada en Wolverine — copiados literalmente de los records en
 * BuildingBlocks/Messaging/SignatureIntegrationEvents/, no adivinados).
 *
 * Hotfix 2026-07-20: documentSignedHandler, requestCompletedHandler,
 * requestCanceledHandler y requestSealedHandler esperaban `createdByUserId` (o, en el
 * caso de documentSignedHandler, el nombre distinto `requestCreatedByUserId`) pero
 * Signature nunca publicaba ese campo — los 4 handlers descartaban el evento en
 * silencio (`if (!requestId || !createdBy) return;`) sin generar ninguna
 * notificación. Este test existe para que, si algún día el nombre del campo vuelve a
 * desalinearse entre .NET y Node, se rompa acá en CI en vez de quedar invisible.
 */
function setup() {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(eventType, handler);
  };
  const notifications: NotificationRepository = {
    createIfMissing: vi.fn().mockResolvedValue(true),
    findById: vi.fn(),
    update: vi.fn(),
    listForUser: vi.fn(),
    countUnread: vi.fn().mockResolvedValue(0),
  };
  const emitter: RealtimeEmitter = {
    emitToUser: vi.fn(),
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToTenant: vi.fn(),
  } as unknown as RealtimeEmitter;

  const customerPortalAccounts: CustomerPortalAccountRepository = {
    upsert: vi.fn(),
    markInactiveByUserId: vi.fn(),
    findActiveByCustomerId: vi.fn().mockResolvedValue(null),
    findActiveByUserId: vi.fn().mockResolvedValue(null),
  };
  const actionMappings: NotificationActionMappingRepository = {
    findByEventKeyAndAudienceRole: vi.fn().mockResolvedValue(null),
    list: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
  };

  bindSignatureConsumers(register, { notifications, emitter, customerPortalAccounts, actionMappings });
  return { handlers, notifications, emitter, customerPortalAccounts, actionMappings };
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
  it('document.signed.v1 crea la notificacion para CreatedByUserId, no para un campo inexistente', async () => {
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
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('preparer-1');
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

  it('document.signed.v1 con cuenta de portal activa notifica a AMBAS audiencias con su propia accion', async () => {
    const { handlers, notifications, customerPortalAccounts, actionMappings } = setup();
    vi.mocked(customerPortalAccounts.findActiveByCustomerId).mockResolvedValue({
      customerId: 'customer-1',
      tenantId: 'tenant-1',
      userId: 'customer-portal-user-1',
      isActive: true,
    });
    vi.mocked(actionMappings.findByEventKeyAndAudienceRole).mockImplementation(async (_eventKey, audienceRole) => {
      if (audienceRole === 'Preparer') {
        return {
          id: 'm1',
          eventKey: 'signature.document.signed.v1',
          audienceRole: 'Preparer',
          actionType: 'DeepLink',
          urlTemplate: '/crm/firmas/{signatureRequestId}',
        };
      }
      return {
        id: 'm2',
        eventKey: 'signature.document.signed.v1',
        audienceRole: 'CustomerSigner',
        actionType: 'None',
        urlTemplate: null,
      };
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
    const preparerCall = calls.find((c) => c[0]!.userId === 'preparer-1c')!;
    const customerCall = calls.find((c) => c[0]!.userId === 'customer-portal-user-1')!;
    expect(preparerCall[0]!.toSnapshot().metadata).toMatchObject({
      actionType: 'DeepLink',
      actionUrl: '/crm/firmas/req-1c',
    });
    expect(customerCall[0]!.toSnapshot().metadata).toMatchObject({ actionType: 'None' });
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
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('preparer-2');
  });

  it('request.canceled.v1 crea la notificacion para CreatedByUserId, no para CanceledByUserId', async () => {
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
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('preparer-3');
  });

  it('request.sealed.v1 crea la notificacion para CreatedByUserId', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('signature.request.sealed.v1')!(
      envelope('signature.request.sealed.v1', {
        SignatureRequestId: 'req-4',
        CreatedByUserId: 'preparer-4',
        SealedFileId: 'file-1',
        DocumentHashPost: 'abc123',
        SealedAtUtc: new Date().toISOString(),
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('preparer-4');
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

  it('signer.rejected.v1 crea la notificacion para CreatedByUserId (antes: sin consumer en absoluto)', async () => {
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
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('preparer-6');
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
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('preparer-7');
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
