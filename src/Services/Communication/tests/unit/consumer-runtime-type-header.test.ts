import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { ConsumeMessage } from 'amqplib';

/**
 * Resolución del tipo de evento en el ConsumerRuntime. Wolverine (.NET) publica el header AMQP
 * `type` de dos formas: como nombre CLR (Auth/CloudStorage/…) o ya como alias del evento
 * (`correspondence.customer_email_received.v1`, Connectors/Correspondence/…). Los handlers se
 * registran por alias. Antes, un header alias que no estaba en CLR_TYPE_TO_EVENT_TYPE dejaba el
 * eventType en undefined → "unmapped; ack to skip" y el handler NUNCA corría: el correo entrante
 * llegaba a la BD pero el front no recibía `mail.incoming` en tiempo real. Este test fija que un
 * header alias resuelve directo al handler registrado.
 */
const channel = {
  prefetch: vi.fn().mockResolvedValue(undefined),
  consume: vi.fn(),
  ack: vi.fn(),
  nack: vi.fn(),
  sendToQueue: vi.fn(),
};

vi.mock('../../src/infrastructure/rabbit/rabbit-connection.js', async (importOriginal) => {
  const actual =
    await importOriginal<typeof import('../../src/infrastructure/rabbit/rabbit-connection.js')>();
  return { ...actual, getRabbitContext: () => ({ channel, connection: {} }) };
});

vi.mock('../../src/infrastructure/config.js', () => ({
  config: {
    rabbitmq: {
      uri: '',
      exchange: 'taxvision-events',
      queue: 'communication-events',
      dlq: 'communication-events.dlq',
    },
  },
}));

vi.mock('../../src/infrastructure/logger/logger.js', () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const { ConsumerRuntime } = await import('../../src/infrastructure/rabbit/consumer-runtime.js');
const { logger } = await import('../../src/infrastructure/logger/logger.js');

/** Mensaje estilo Wolverine .NET: el tipo va SOLO en el header AMQP `type`, no en el body. */
function message(amqpType: string | undefined, body: Record<string, unknown>): ConsumeMessage {
  return {
    content: Buffer.from(JSON.stringify(body)),
    fields: {} as ConsumeMessage['fields'],
    properties: { type: amqpType, headers: {} } as ConsumeMessage['properties'],
  };
}

async function startRuntime(eventType = 'correspondence.customer_email_received.v1') {
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
  const handler = vi.fn().mockResolvedValue(undefined);
  runtime.register(eventType, handler);
  await runtime.start();

  const dispatch = async (msg: ConsumeMessage) => {
    deliver(msg);
    for (let i = 0; i < 10; i++) await Promise.resolve();
  };
  return { dispatch, handler };
}

beforeEach(() => {
  channel.ack.mockClear();
  channel.nack.mockClear();
  channel.consume.mockReset();
  vi.mocked(logger.warn).mockClear();
});

describe('ConsumerRuntime — resolución del header AMQP `type`', () => {
  it('un header alias (.v1) fuera del mapa CLR resuelve al handler registrado por alias', async () => {
    const { dispatch, handler } = await startRuntime();

    await dispatch(
      message('correspondence.customer_email_received.v1', {
        EventId: 'evt-1',
        TenantId: 'tenant-1',
        CustomerId: 'customer-1',
      }),
    );

    expect(handler).toHaveBeenCalledTimes(1);
    const env = handler.mock.calls[0]![0];
    expect(env.eventType).toBe('correspondence.customer_email_received.v1');
    expect(env.tenantId).toBe('tenant-1');
    expect(channel.ack).toHaveBeenCalledTimes(1);
  });

  it('un header sin handler registrado se advierte una vez por ventana y se ackea', async () => {
    const { dispatch, handler } = await startRuntime();

    await dispatch(message('cloudstorage.file_available.v1', { EventId: 'evt-2', TenantId: 'tenant-1' }));
    await dispatch(message('cloudstorage.file_available.v1', { EventId: 'evt-3', TenantId: 'tenant-1' }));

    expect(handler).not.toHaveBeenCalled();
    expect(channel.ack).toHaveBeenCalledTimes(2);
    expect(channel.nack).not.toHaveBeenCalled();
    expect(logger.warn).toHaveBeenCalledTimes(1);
    expect(logger.warn).toHaveBeenCalledWith(
      {
        eventType: 'cloudstorage.file_available.v1',
        amqpType: 'cloudstorage.file_available.v1',
        eventId: 'evt-2',
      },
      'consumer: event type has no registered handler; ack to skip',
    );
  });

  it('no advierte por un alias ignorado explicitamente', async () => {
    const { dispatch } = await startRuntime();

    // signature.signer.invited.v1 salió del ignore set en T7.4.C — Notification
    // ahora lo procesa. Usamos signature.request.reminder_due.v1 que sigue
    // ignorado para probar el camino de alias explícitamente excluido.
    await dispatch(message('signature.request.reminder_due.v1', { EventId: 'evt-ignored', TenantId: 'tenant-1' }));

    expect(logger.warn).not.toHaveBeenCalled();
    expect(channel.ack).toHaveBeenCalledTimes(1);
  });

  it('mapea ModuleCategoryChanged de Wolverine al alias del consumer', async () => {
    const eventType = 'subscription.module_category_changed.v1';
    const { dispatch, handler } = await startRuntime(eventType);

    await dispatch(
      message(
        'BuildingBlocks.Messaging.SubscriptionIntegrationEvents.ModuleCategoryChangedIntegrationEvent',
        {
          EventId: 'evt-module-1',
          TenantId: '8f58a521-4c25-4d91-9f4e-7ad5df14c001',
          ModuleCode: 'wallet',
        },
      ),
    );

    expect(handler).toHaveBeenCalledTimes(1);
    expect(handler.mock.calls[0]![0].eventType).toBe(eventType);
  });
});
