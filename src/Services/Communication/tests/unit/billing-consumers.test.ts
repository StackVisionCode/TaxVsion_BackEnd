import { describe, expect, it, vi } from 'vitest';
import { bindBillingConsumers } from '../../src/application/event-handlers/billing-consumers.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import { BillingSocketEvents } from '../../src/contracts/socket/billing-socket-events.js';

const TENANT = '8f58a521-4c25-4d91-9f4e-7ad5df14c001';

function setup() {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const emitToTenantStaff = vi.fn();
  const emitter = { emitToTenantStaff } as unknown as RealtimeEmitter;
  bindBillingConsumers((type, handler) => handlers.set(type, handler), { emitter });
  return { handlers, emitToTenantStaff };
}

function envelope(payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'e1',
    eventType: 'billing.invoice.status_changed',
    tenantId: TENANT,
    correlationId: 'c1',
    occurredOnUtc: '2026-11-18T12:00:00Z',
    payload,
  };
}

describe('billing realtime relay', () => {
  it('registers under both the body eventType and the header alias', () => {
    const { handlers } = setup();
    expect([...handlers.keys()].sort()).toEqual([
      'billing.invoice.status_changed',
      'billing.invoice.status_changed.v1',
    ]);
  });

  it('relays a status change to the tenant staff room as invoice.updated with a minimal payload', async () => {
    const { handlers, emitToTenantStaff } = setup();

    await handlers.get('billing.invoice.status_changed.v1')!(
      envelope({
        invoiceId: 'inv-1',
        invoiceNumber: 'INV-2026-00002',
        fromStatus: 'Sent',
        toStatus: 'Paid',
        trigger: 'Payment',
        someInternalField: 'must not leak',
      }),
    );

    expect(emitToTenantStaff).toHaveBeenCalledTimes(1);
    const call = emitToTenantStaff.mock.calls[0]![0];
    expect(call.tenantId).toBe(TENANT);
    expect(call.event).toBe(BillingSocketEvents.InvoiceUpdated);
    expect(call.envelope.payload).toEqual({
      invoiceId: 'inv-1',
      invoiceNumber: 'INV-2026-00002',
      fromStatus: 'Sent',
      status: 'Paid',
      trigger: 'Payment',
    });
    expect(call.envelope.correlationId).toBe('c1');
  });

  it('accepts PascalCase fields and a creation event without a previous status', async () => {
    const { handlers, emitToTenantStaff } = setup();

    await handlers.get('billing.invoice.status_changed')!(
      envelope({ InvoiceId: 'inv-2', ToStatus: 'Draft', Trigger: 'Created' }),
    );

    expect(emitToTenantStaff.mock.calls[0]![0].envelope.payload).toEqual({
      invoiceId: 'inv-2',
      invoiceNumber: null,
      fromStatus: null,
      status: 'Draft',
      trigger: 'Created',
    });
  });

  it('drops a message without invoice or status instead of emitting garbage', async () => {
    const { handlers, emitToTenantStaff } = setup();

    await handlers.get('billing.invoice.status_changed.v1')!(envelope({ toStatus: 'Paid' }));
    await handlers.get('billing.invoice.status_changed.v1')!(envelope({ invoiceId: 'inv-3' }));

    expect(emitToTenantStaff).not.toHaveBeenCalled();
  });
});
