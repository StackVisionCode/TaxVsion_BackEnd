import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ConsumeMessage } from 'amqplib';

/**
 * H-15 — politica de fallo del consumer.
 *
 * Hasta 2026-08-07 un handler que fallaba mandaba el mensaje a la DLQ al primer intento,
 * mientras el mismo tipo de fallo en los 17 servicios .NET se reintentaba 4 veces. La asimetria
 * no la habia elegido nadie: cada lado heredo el default de su libreria. Estos tests fijan que
 * los dos lados hacen ahora 1 intento + 3 cooldowns antes de rendirse.
 *
 * El detalle que mas facil se rompe al tocar este codigo es el `unmark` del inbox: sin el, el
 * reintento se ve como duplicado y se ack-skipea, con lo que la cadena de reintentos existe
 * pero no reintenta nada. Por eso tiene su propio test.
 */
const channel = {
  prefetch: vi.fn().mockResolvedValue(undefined),
  consume: vi.fn(),
  ack: vi.fn(),
  nack: vi.fn(),
  sendToQueue: vi.fn(),
};

vi.mock('../../src/infrastructure/rabbit/rabbit-connection.js', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../src/infrastructure/rabbit/rabbit-connection.js')>();
  return { ...actual, getRabbitContext: () => ({ channel, connection: {} }) };
});

vi.mock('../../src/infrastructure/config.js', () => ({
  config: { rabbitmq: { uri: '', exchange: 'taxvision-events', queue: 'communication-events', dlq: 'communication-events.dlq' } },
}));

// El logger real construye pino leyendo config.log, que el mock de arriba no trae.
vi.mock('../../src/infrastructure/logger/logger.js', () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const { ConsumerRuntime } = await import('../../src/infrastructure/rabbit/consumer-runtime.js');
const { ATTEMPT_HEADER, RETRY_COOLDOWNS_MS, retryQueueName } = await import(
  '../../src/infrastructure/rabbit/rabbit-connection.js'
);

function message(attempt?: number): ConsumeMessage {
  return {
    content: Buffer.from(
      JSON.stringify({ eventId: 'evt-1', eventType: 'test.boom.v1', tenantId: 'tenant-1' }),
    ),
    fields: {} as ConsumeMessage['fields'],
    properties: {
      headers: attempt === undefined ? {} : { [ATTEMPT_HEADER]: attempt },
    } as ConsumeMessage['properties'],
  };
}

/** Arranca el runtime con un handler que siempre falla y devuelve el callback de consume. */
async function startWithFailingHandler() {
  const processedEvents = {
    tryMarkProcessed: vi.fn().mockResolvedValue(true),
    unmark: vi.fn().mockResolvedValue(undefined),
  };
  let deliver!: (msg: ConsumeMessage) => void;
  channel.consume.mockImplementation((_queue: string, cb: (msg: ConsumeMessage) => void) => {
    deliver = cb;
    return Promise.resolve({ consumerTag: 'tag-1' });
  });

  const runtime = new ConsumerRuntime(processedEvents as never);
  runtime.register('test.boom.v1', () => Promise.reject(new Error('fallo de prueba')));
  await runtime.start();

  const dispatch = async (msg: ConsumeMessage) => {
    deliver(msg);
    // dispatch corre en background (void this.dispatch(msg)); cedemos el turno hasta que drena.
    for (let i = 0; i < 10; i++) await Promise.resolve();
  };
  return { dispatch, processedEvents };
}

beforeEach(() => {
  channel.ack.mockClear();
  channel.nack.mockClear();
  channel.sendToQueue.mockClear();
  channel.consume.mockReset();
});

describe('ConsumerRuntime — politica de fallo (H-15)', () => {
  it('el primer fallo va a la cola de espera del cooldown 1, no a la DLQ', async () => {
    const { dispatch } = await startWithFailingHandler();

    await dispatch(message());

    expect(channel.nack).not.toHaveBeenCalled();
    expect(channel.sendToQueue).toHaveBeenCalledTimes(1);
    const [queue, , options] = channel.sendToQueue.mock.calls[0]!;
    expect(queue).toBe(retryQueueName(1));
    expect(options.headers[ATTEMPT_HEADER]).toBe(1);
    // Ack DESPUES de encolar la copia: al reves el mensaje se perderia si el proceso muere.
    expect(channel.ack).toHaveBeenCalledTimes(1);
  });

  it('cada reintento avanza a la siguiente cola de espera', async () => {
    const { dispatch } = await startWithFailingHandler();

    for (let attempt = 1; attempt < RETRY_COOLDOWNS_MS.length; attempt++) {
      channel.sendToQueue.mockClear();
      await dispatch(message(attempt));
      const [queue, , options] = channel.sendToQueue.mock.calls[0]!;
      expect(queue).toBe(retryQueueName(attempt + 1));
      expect(options.headers[ATTEMPT_HEADER]).toBe(attempt + 1);
    }
    expect(channel.nack).not.toHaveBeenCalled();
  });

  it('agotados los cooldowns, va a la DLQ sin requeue', async () => {
    const { dispatch } = await startWithFailingHandler();

    await dispatch(message(RETRY_COOLDOWNS_MS.length));

    expect(channel.sendToQueue).not.toHaveBeenCalled();
    expect(channel.nack).toHaveBeenCalledWith(expect.anything(), false, false);
  });

  it('desmarca el inbox en cada fallo, o el reintento se veria como duplicado', async () => {
    const { dispatch, processedEvents } = await startWithFailingHandler();

    await dispatch(message());

    expect(processedEvents.unmark).toHaveBeenCalledWith({ eventId: 'evt-1', source: 'test' });
  });
});
