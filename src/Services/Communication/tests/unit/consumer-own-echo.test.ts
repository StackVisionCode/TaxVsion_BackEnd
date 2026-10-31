import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ConsumeMessage } from 'amqplib';

/**
 * Communication publica al fanout compartido `taxvision-events` (regla del dueño: no inventar exchanges)
 * y el fanout le devuelve sus propios eventos. Un eco propio sin handler (p. ej. el pedido de transcripción
 * de una nota de voz, que consume el worker) se descarta sin warn; uno de otro servicio sin handler sí avisa.
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
  config: {
    serviceName: 'communication-service',
    rabbitmq: { uri: '', exchange: 'taxvision-events', queue: 'communication-events', dlq: 'communication-events.dlq' },
  },
}));

vi.mock('../../src/infrastructure/logger/logger.js', () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const { ConsumerRuntime } = await import('../../src/infrastructure/rabbit/consumer-runtime.js');
const { logger } = await import('../../src/infrastructure/logger/logger.js');

function message(eventType: string, appId: string | undefined): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify({ eventId: `evt-${eventType}-${appId}`, eventType, tenantId: 't1', payload: {} })),
    fields: {} as ConsumeMessage['fields'],
    properties: { type: eventType, appId, headers: {} } as ConsumeMessage['properties'],
  };
}

async function startRuntime() {
  let deliver!: (msg: ConsumeMessage) => void;
  channel.consume.mockImplementation((_queue: string, cb: (msg: ConsumeMessage) => void) => {
    deliver = cb;
    return Promise.resolve({ consumerTag: 'tag-1' });
  });
  const runtime = new ConsumerRuntime({ tryMarkProcessed: vi.fn().mockResolvedValue(true), unmark: vi.fn() } as never);
  await runtime.start();
  return async (msg: ConsumeMessage) => {
    deliver(msg);
    for (let i = 0; i < 10; i++) await Promise.resolve();
  };
}

beforeEach(() => {
  channel.ack.mockClear();
  channel.consume.mockReset();
  vi.mocked(logger.warn).mockClear();
});

describe('ConsumerRuntime — eco propio del fanout', () => {
  it('el pedido de transcripción que publicó Communication se descarta sin warn', async () => {
    const dispatch = await startRuntime();

    await dispatch(message('communication.chat.voice_note_transcription_requested.v1', 'communication-service'));
    await dispatch(message('communication.meeting.recording_ready.v1', 'communication-service'));

    expect(channel.ack).toHaveBeenCalledTimes(2);
    expect(logger.warn).not.toHaveBeenCalled();
  });

  it('un evento de otro servicio sin handler sigue avisando (no se esconde un handler que falte)', async () => {
    const dispatch = await startRuntime();

    await dispatch(message('connectors.raw_message_received.v1', undefined));

    expect(channel.ack).toHaveBeenCalledTimes(1);
    expect(logger.warn).toHaveBeenCalledWith(
      expect.objectContaining({ eventType: 'connectors.raw_message_received.v1' }),
      'consumer: event type has no registered handler; ack to skip',
    );
  });
});
