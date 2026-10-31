import { describe, expect, it } from 'vitest';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { SfuTransportStateChange } from '../../src/application/ports/sfu-service.js';
import {
  buildTransportFailedEvent,
  createSfuTransportStateReporter,
  isTransportFailure,
} from '../../src/application/use-cases/report-sfu-transport-state.js';

const base: SfuTransportStateChange = {
  tenantId: 't1',
  meetingId: 'm1',
  userId: 'u1',
  direction: 'send',
  transportId: 'tr1',
  layer: 'ice',
  state: 'connected',
};

describe('report-sfu-transport-state', () => {
  it('solo DTLS failed e ICE disconnected cuentan como fallo (mediasoup es ICE-lite)', () => {
    expect(isTransportFailure({ ...base, layer: 'ice', state: 'connected' })).toBe(false);
    expect(isTransportFailure({ ...base, layer: 'ice', state: 'completed' })).toBe(false);
    expect(isTransportFailure({ ...base, layer: 'ice', state: 'disconnected' })).toBe(true);
    expect(isTransportFailure({ ...base, layer: 'dtls', state: 'connecting' })).toBe(false);
    expect(isTransportFailure({ ...base, layer: 'dtls', state: 'failed' })).toBe(true);
  });

  it('publica transport_failed con tenant, meeting, direccion y capa', async () => {
    const published: IntegrationEvent[] = [];
    const reporter = createSfuTransportStateReporter({ publisher: { enqueue: async (e) => void published.push(e) } });
    reporter.onTransportStateChanged({ ...base, state: 'connected' });
    reporter.onTransportStateChanged({ ...base, layer: 'dtls', state: 'failed' });
    await Promise.resolve();
    expect(published).toHaveLength(1);
    expect(published[0]).toMatchObject({
      eventType: 'communication.meeting.transport_failed.v1',
      tenantId: 't1',
      meetingId: 'm1',
      participantUserId: 'u1',
      direction: 'send',
      layer: 'dtls',
      correlationId: 'tr1',
    });
  });

  it('el evento lleva la fecha del fallo en ISO', () => {
    const now = new Date('2026-10-29T12:00:00.000Z');
    const event = buildTransportFailedEvent({ ...base, layer: 'ice', state: 'disconnected' }, now);
    expect(event.failedAtUtc).toBe('2026-10-29T12:00:00.000Z');
    expect(event.occurredOnUtc).toBe('2026-10-29T12:00:00.000Z');
  });
});
