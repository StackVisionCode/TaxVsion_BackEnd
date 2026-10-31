import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ConsumeMessage } from 'amqplib';

/**
 * F10 (ADD-08): proyecciones de lectura (analítica) que conviven con el handler principal del mismo
 * evento. Corren después de él; si fallan, el mensaje se ackea igual (reintentar repetiría los efectos
 * del principal) y queda la métrica.
 */
const channel = {
  prefetch: vi.fn().mockResolvedValue(undefined),
  consume: vi.fn(),
  ack: vi.fn(),
  nack: vi.fn(),
  sendToQueue: vi.fn(),
};

vi.mock('../../src/infrastructure/rabbit/rabbit-connection.js', async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return { ...actual, getRabbitContext: () => ({ channel, connection: {} }) };
});

vi.mock('../../src/infrastructure/config.js', () => ({
  config: { rabbitmq: { uri: '', exchange: 'taxvision-events', queue: 'communication-events', dlq: 'communication-events.dlq' } },
}));

vi.mock('../../src/infrastructure/logger/logger.js', () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const { ConsumerRuntime } = await import('../../src/infrastructure/rabbit/consumer-runtime.js');
const { consumerProjectionFailuresTotal } = await import('../../src/infrastructure/telemetry/metrics.js');

const EVENT = 'communication.meeting.recording_ready.v1';

function message(): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify({ eventId: 'e-1', eventType: EVENT, tenantId: 't-1', occurredOnUtc: '2026-10-31T00:00:00Z' })),
    fields: {} as ConsumeMessage['fields'],
    properties: { headers: {} } as ConsumeMessage['properties'],
  };
}

async function start(register: (runtime: InstanceType<typeof ConsumerRuntime>) => void) {
  let deliver!: (msg: ConsumeMessage) => void;
  channel.consume.mockImplementation((_queue: string, cb: (msg: ConsumeMessage) => void) => {
    deliver = cb;
    return Promise.resolve({ consumerTag: 'tag-1' });
  });
  const runtime = new ConsumerRuntime({ tryMarkProcessed: vi.fn().mockResolvedValue(true), unmark: vi.fn().mockResolvedValue(undefined) } as never);
  register(runtime);
  await runtime.start();
  return async () => {
    deliver(message());
    for (let i = 0; i < 20; i++) await Promise.resolve();
  };
}

beforeEach(() => {
  channel.ack.mockClear();
  channel.nack.mockClear();
  channel.sendToQueue.mockClear();
});

describe('ConsumerRuntime projections', () => {
  it('el principal corre primero y luego todas las proyecciones del evento', async () => {
    const order: string[] = [];
    const dispatch = await start((runtime) => {
      runtime.register(EVENT, async () => void order.push('main'));
      runtime.registerProjection(EVENT, async () => void order.push('analytics'));
      runtime.registerProjection(EVENT, async () => void order.push('other'));
    });
    await dispatch();
    expect(order).toEqual(['main', 'analytics', 'other']);
    expect(channel.ack).toHaveBeenCalledTimes(1);
  });

  it('una proyección que falla no reintenta el mensaje ni frena a las demás', async () => {
    const before = (await consumerProjectionFailuresTotal.get()).values.find((v) => v.labels.event_type === EVENT)?.value ?? 0;
    const after = vi.fn().mockResolvedValue(undefined);
    const dispatch = await start((runtime) => {
      runtime.register(EVENT, vi.fn().mockResolvedValue(undefined));
      runtime.registerProjection(EVENT, vi.fn().mockRejectedValue(new Error('db down')));
      runtime.registerProjection(EVENT, after);
    });
    await dispatch();
    expect(after).toHaveBeenCalled();
    expect(channel.ack).toHaveBeenCalledTimes(1);
    expect(channel.sendToQueue).not.toHaveBeenCalled();
    const now = (await consumerProjectionFailuresTotal.get()).values.find((v) => v.labels.event_type === EVENT)?.value ?? 0;
    expect(now - before).toBe(1);
  });

  it('un evento con solo proyecciones (sin principal) también se procesa', async () => {
    const projection = vi.fn().mockResolvedValue(undefined);
    const dispatch = await start((runtime) => runtime.registerProjection(EVENT, projection));
    await dispatch();
    expect(projection).toHaveBeenCalled();
    expect(channel.ack).toHaveBeenCalledTimes(1);
  });

  it('si el principal falla, las proyecciones no corren (el reintento las correrá con él)', async () => {
    const projection = vi.fn();
    const dispatch = await start((runtime) => {
      runtime.register(EVENT, vi.fn().mockRejectedValue(new Error('boom')));
      runtime.registerProjection(EVENT, projection);
    });
    await dispatch();
    expect(projection).not.toHaveBeenCalled();
    expect(channel.sendToQueue).toHaveBeenCalledTimes(1);
  });
});
