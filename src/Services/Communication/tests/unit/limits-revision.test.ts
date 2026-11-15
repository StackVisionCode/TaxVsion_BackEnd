import { describe, expect, it, vi } from 'vitest';
import { bindSubscriptionConsumers } from '../../src/application/event-handlers/subscription-consumers.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';

/**
 * Análisis 2026-11-11: los límites del plan se guardaban sin la revisión de entitlements, así que un evento
 * viejo que llegaba tarde los pisaba. El consumidor tiene que pasar la revisión al repositorio.
 */
describe('subscription.entitlements_changed — revisión de los límites', () => {
  it('pasa la revisión del evento al repositorio de límites', async () => {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const upsert = vi.fn();
    bindSubscriptionConsumers((eventType, handler) => void handlers.set(eventType, handler), {
      limits: { findByTenantId: vi.fn(), upsert } as never,
    } as never);

    const handler = handlers.get('subscription.entitlements_changed.v1');
    expect(handler).toBeDefined();
    await handler?.({
      eventId: 'evt-1',
      eventType: 'subscription.entitlements_changed.v1',
      tenantId: 'tenant-1',
      correlationId: 'corr-1',
      occurredOnUtc: new Date().toISOString(),
      payload: { PlanCode: 'enterprise', RevisionNumber: 7, EntitlementValues: {} },
    });

    expect(upsert).toHaveBeenCalledWith(expect.objectContaining({ revisionNumber: 7 }));
  });

  // P11: el inicio del período es el ancla del cupo mensual; un DateTime de .NET sin zona se lee como UTC.
  it('guarda el inicio del período como ancla del cupo, en UTC', async () => {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const upsert = vi.fn();
    bindSubscriptionConsumers((eventType, handler) => void handlers.set(eventType, handler), {
      limits: { findByTenantId: vi.fn(), upsert } as never,
    } as never);

    await handlers.get('subscription.entitlements_changed.v1')?.({
      eventId: 'evt-2',
      eventType: 'subscription.entitlements_changed.v1',
      tenantId: 'tenant-1',
      correlationId: 'corr-2',
      occurredOnUtc: new Date().toISOString(),
      payload: { PlanCode: 'enterprise', RevisionNumber: 8, EntitlementValues: {}, CurrentPeriodStartUtc: '2026-03-15T14:00:00' },
    });

    expect(upsert).toHaveBeenCalledWith(expect.objectContaining({ usagePeriodAnchorUtc: new Date('2026-03-15T14:00:00Z') }));
  });
});
