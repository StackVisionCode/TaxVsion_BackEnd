import { describe, expect, it, vi } from 'vitest';
import { bindConnectorsConsumers } from '../../src/application/event-handlers/connectors-consumers.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';

/**
 * Test de contrato (regla de la Fase 0 del plan de notificaciones): los payloads de
 * abajo usan los nombres de campo EXACTOS que .NET serializa hoy (PascalCase,
 * copiados literalmente de los records en
 * BuildingBlocks/Messaging/ConnectorsIntegrationEvents/ — Fase 1B: ninguno de estos
 * 2 eventos tenia consumer en ningun servicio antes de esta fase, ni siquiera el
 * campo CreatedByUserId existia).
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

  bindConnectorsConsumers(register, { notifications, emitter });
  return { handlers, notifications, emitter };
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

describe('bindConnectorsConsumers — contrato de campos con Connectors (.NET)', () => {
  it('oauth.refresh_failed.v1 crea la notificacion para CreatedByUserId', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('connectors.oauth.refresh_failed.v1')!(
      envelope('connectors.oauth.refresh_failed.v1', {
        AccountId: 'account-1',
        ConnectionId: 'conn-1',
        ProviderCode: 'Gmail',
        Reason: 'invalid_grant',
        ErrorCode: 'invalid_grant',
        FailedAtUtc: new Date().toISOString(),
        CreatedByUserId: 'connector-owner-1',
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('connector-owner-1');
  });

  it('watch.expired.v1 crea la notificacion para CreatedByUserId', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('connectors.watch.expired.v1')!(
      envelope('connectors.watch.expired.v1', {
        AccountId: 'account-2',
        SubscriptionId: 'sub-1',
        ProviderCode: 'Graph',
        FailureCount: 3,
        ExpiredAtUtc: new Date().toISOString(),
        CreatedByUserId: 'connector-owner-2',
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('connector-owner-2');
  });

  it('no crea ninguna notificacion si CreatedByUserId falta', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('connectors.oauth.refresh_failed.v1')!(
      envelope('connectors.oauth.refresh_failed.v1', {
        AccountId: 'account-3',
        ConnectionId: 'conn-2',
        ProviderCode: 'Gmail',
        Reason: 'invalid_grant',
        ErrorCode: 'invalid_grant',
        FailedAtUtc: new Date().toISOString(),
      }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });
});
