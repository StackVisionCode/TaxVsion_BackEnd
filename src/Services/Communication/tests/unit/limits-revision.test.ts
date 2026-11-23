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

  // Plan a la medida: el evento dice qué claves son excepción de la oficina; un evento viejo sin la lista = ninguna.
  it('guarda qué claves son excepción de la oficina', async () => {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const upsert = vi.fn();
    bindSubscriptionConsumers((eventType, handler) => void handlers.set(eventType, handler), {
      limits: { findByTenantId: vi.fn(), upsert } as never,
    } as never);
    const handler = handlers.get('subscription.entitlements_changed.v1');
    const envelope = (payload: Record<string, unknown>): IncomingEnvelope => ({
      eventId: 'evt-3',
      eventType: 'subscription.entitlements_changed.v1',
      tenantId: 'tenant-1',
      correlationId: 'corr-3',
      occurredOnUtc: new Date().toISOString(),
      payload,
    });

    await handler?.(envelope({ PlanCode: 'enterprise', RevisionNumber: 9, EntitlementValues: {}, OverriddenKeys: ['communication.max_meeting_minutes'] }));
    await handler?.(envelope({ PlanCode: 'enterprise', RevisionNumber: 10, EntitlementValues: {} }));

    expect(upsert).toHaveBeenNthCalledWith(1, expect.objectContaining({ overriddenKeys: ['communication.max_meeting_minutes'] }));
    expect(upsert).toHaveBeenNthCalledWith(2, expect.objectContaining({ overriddenKeys: [] }));
  });
});
