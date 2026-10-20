import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it, vi } from 'vitest';
import {
  bindInAppNotificationConsumers,
  parseInAppNotificationRequested,
} from '../../src/application/event-handlers/notification-inapp-consumers.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { InAppNotificationRepository } from '../../src/application/ports/in-app-notification-repository.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { NotificationSocketEvents } from '../../src/contracts/socket/notification-socket-events.js';

const fixturePath = fileURLToPath(
  new URL(
    '../../../../BuildingBlocks/Messaging/Contracts/Fixtures/notification.inapp.requested.v1.json',
    import.meta.url,
  ),
);
const fixture = JSON.parse(readFileSync(fixturePath, 'utf8')) as Record<string, unknown>;

function setup(status: 'created' | 'updated' | 'duplicate' = 'created') {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const notifications = {
    persist: vi.fn(async (notification) => ({
      status,
      notification: status === 'duplicate' ? null : notification.toSnapshot(),
    })),
    countUnread: vi.fn().mockResolvedValue(4),
    markUnread: vi.fn().mockResolvedValue(true),
    markManyRead: vi.fn().mockResolvedValue({ markedCount: 0, unreadCount: 0 }),
    markManyUnread: vi.fn().mockResolvedValue({ unmarkedCount: 0, unreadCount: 0 }),
    dismissMany: vi.fn().mockResolvedValue({ dismissedCount: 0, unreadCount: 0 }),
    countForUser: vi.fn().mockResolvedValue(4),
  } as InAppNotificationRepository;
  const emitter = { emitToUser: vi.fn() } as unknown as RealtimeEmitter;
  bindInAppNotificationConsumers((eventType, handler) => handlers.set(eventType, handler), {
    notifications,
    emitter,
  });
  return { handlers, notifications, emitter };
}

function envelope(payload: Record<string, unknown> = fixture): IncomingEnvelope {
  return {
    eventId: String(payload['EventId']),
    eventType: 'notification.inapp.requested.v1',
    tenantId: String(payload['TenantId']),
    correlationId: String(payload['CorrelationId']),
    occurredOnUtc: String(payload['OccurredOn']),
    payload,
  };
}

describe('notification.inapp.requested.v1', () => {
  it('acepta el golden JSON emitido por .NET', () => {
    const result = parseInAppNotificationRequested(fixture);

    expect(result.success).toBe(true);
  });

  it('persiste y emite una notificacion nueva', async () => {
    const { handlers, notifications, emitter } = setup();

    await handlers.get('notification.inapp.requested.v1')!(envelope());

    expect(notifications.persist).toHaveBeenCalledTimes(1);
    expect(emitter.emitToUser).toHaveBeenCalledTimes(2);
    expect(vi.mocked(emitter.emitToUser).mock.calls[0]![0]!.event).toBe(NotificationSocketEvents.Received);
  });

  it('emite notification.updated cuando agrupa una notificacion no leida', async () => {
    const { handlers, emitter } = setup('updated');

    await handlers.get('notification.inapp.requested.v1')!(envelope());

    expect(vi.mocked(emitter.emitToUser).mock.calls[0]![0]!.event).toBe(NotificationSocketEvents.Updated);
  });

  it('no reemite una entrega duplicada', async () => {
    const { handlers, emitter } = setup('duplicate');

    await handlers.get('notification.inapp.requested.v1')!(envelope());

    expect(emitter.emitToUser).not.toHaveBeenCalled();
  });

  it('trunca strings largos en vez de rechazar el payload a DLQ', () => {
    const long = (n: number) => 'a'.repeat(n);
    const payload = {
      ...fixture,
      FallbackTitle: long(500),
      FallbackBody: long(2000),
      Kind: long(100),
      GroupKey: long(400),
    };

    const result = parseInAppNotificationRequested(payload);

    expect(result.success).toBe(true);
    if (!result.success) return;
    expect(result.data.fallbackTitle.length).toBe(200);
    expect(result.data.fallbackBody?.length).toBe(1000);
    expect(result.data.kind.length).toBe(64);
    expect(result.data.groupKey?.length).toBe(256);
  });

  it('un fallbackBody vacio cae a null (y el consumer usa fallbackTitle)', () => {
    const result = parseInAppNotificationRequested({ ...fixture, FallbackBody: '   ' });

    expect(result.success).toBe(true);
    if (!result.success) return;
    expect(result.data.fallbackBody ?? null).toBeNull();
  });

  it('parsea el formato real del cable (camelCase) emitido por Wolverine', () => {
    const camelCase = Object.fromEntries(
      Object.entries(fixture).map(([key, value]) => {
        const camelKey = key.charAt(0).toLowerCase() + key.slice(1);
        return [camelKey, value];
      }),
    );

    const result = parseInAppNotificationRequested(camelCase);

    expect(result.success).toBe(true);
    if (!result.success) return;
    expect(result.data.recipientUserId).toBe(fixture['RecipientUserId']);
    expect(result.data.kind).toBe(fixture['Kind']);
    expect(result.data.resourceType).toBe(fixture['ResourceType']);
    expect(result.data.action).toBe(fixture['Action']);
  });
});
