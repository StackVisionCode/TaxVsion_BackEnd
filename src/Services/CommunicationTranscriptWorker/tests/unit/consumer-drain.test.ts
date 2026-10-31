import { describe, it, expect, vi, beforeEach } from 'vitest';
import { randomUUID } from 'node:crypto';
import type { TranscriptInbox } from '../../src/redis/transcript-inbox.js';

/**
 * Fase Transcript 4 — graceful drain: `stop()` debe (1) cancelar el consumer
 * tag inmediatamente (no mas mensajes nuevos) y (2) esperar a que el/los
 * job(s) en curso terminen antes de resolver, hasta un timeout maximo.
 */

const mocks = vi.hoisted(() => ({
  getRabbitContext: vi.fn(),
  reconnectListeners: [] as Array<() => Promise<void> | void>,
}));

vi.mock('../../src/rabbit/rabbit-connection.js', () => ({
  getRabbitContext: mocks.getRabbitContext,
  onRabbitReconnected: (listener: () => Promise<void> | void) => mocks.reconnectListeners.push(listener),
}));

const { startConsumer } = await import('../../src/rabbit/consumer.js');

function u(): string {
  return randomUUID();
}

interface FakeChannel {
  consumerTag: string;
  onMessage: ((msg: unknown) => void) | undefined;
  prefetch: (n: number) => Promise<void>;
  consume: (queue: string, cb: (msg: unknown) => void) => Promise<{ consumerTag: string }>;
  cancel: ReturnType<typeof vi.fn>;
  ack: ReturnType<typeof vi.fn>;
  nack: ReturnType<typeof vi.fn>;
}

function makeFakeChannel(consumerTag = 'ctag-1'): FakeChannel {
  const channel: FakeChannel = {
    consumerTag,
    onMessage: undefined,
    prefetch: async () => undefined,
    consume: async (_queue, cb) => {
      channel.onMessage = cb;
      return { consumerTag: channel.consumerTag };
    },
    cancel: vi.fn(async () => undefined),
    ack: vi.fn(),
    nack: vi.fn(),
  };
  return channel;
}

function fakeMessage(payload: Record<string, unknown>): { content: Buffer } {
  return { content: Buffer.from(JSON.stringify(payload), 'utf-8') };
}

function fakeInbox(): TranscriptInbox {
  return {
    tryMarkProcessed: async () => true,
    unmark: async () => undefined,
  } as unknown as TranscriptInbox;
}

async function flushMicrotasks(times = 5): Promise<void> {
  for (let i = 0; i < times; i += 1) {
    await Promise.resolve();
  }
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.reconnectListeners.length = 0;
});

async function reconnect(): Promise<void> {
  for (const listener of mocks.reconnectListeners) await listener();
}

function voiceNoteRequest(): { content: Buffer } {
  return fakeMessage({
    eventType: 'communication.chat.voice_note_transcription_requested.v1',
    eventId: u(),
    tenantId: u(),
    conversationId: u(),
    messageId: u(),
    fileId: u(),
  });
}

describe('startConsumer tras una reconexión de RabbitMQ', () => {
  it('vuelve a suscribirse en el canal nuevo y confirma cada mensaje en el canal que lo entregó', async () => {
    const oldChannel = makeFakeChannel('ctag-old');
    mocks.getRabbitContext.mockReturnValue({ channel: oldChannel, connection: {} });
    const handler = vi.fn(async () => undefined);
    await startConsumer(handler, { inbox: fakeInbox() });

    const newChannel = makeFakeChannel('ctag-new');
    mocks.getRabbitContext.mockReturnValue({ channel: newChannel, connection: {} });
    await reconnect();
    expect(newChannel.onMessage).toBeDefined();

    newChannel.onMessage?.(voiceNoteRequest());
    await flushMicrotasks();
    expect(handler).toHaveBeenCalledTimes(1);
    expect(newChannel.ack).toHaveBeenCalledTimes(1);
    expect(oldChannel.ack).not.toHaveBeenCalled();
  });

  it('si el canal que entregó ya cayó, no revienta: el broker lo reencola', async () => {
    const oldChannel = makeFakeChannel('ctag-old');
    oldChannel.ack.mockImplementation(() => {
      throw new Error('Channel closed');
    });
    mocks.getRabbitContext.mockReturnValue({ channel: oldChannel, connection: {} });
    await startConsumer(vi.fn(async () => undefined), { inbox: fakeInbox() });

    oldChannel.onMessage?.(voiceNoteRequest());
    await expect(flushMicrotasks()).resolves.toBeUndefined();
    expect(oldChannel.ack).toHaveBeenCalledTimes(1);
  });

  it('después de stop() una reconexión no vuelve a suscribirse', async () => {
    const oldChannel = makeFakeChannel('ctag-old');
    mocks.getRabbitContext.mockReturnValue({ channel: oldChannel, connection: {} });
    const consumer = await startConsumer(vi.fn(async () => undefined), { inbox: fakeInbox() });
    await consumer.stop(1_000);

    const newChannel = makeFakeChannel('ctag-new');
    mocks.getRabbitContext.mockReturnValue({ channel: newChannel, connection: {} });
    await reconnect();
    expect(newChannel.onMessage).toBeUndefined();
  });
});

describe('startConsumer / stop() — graceful drain (Fase Transcript 4)', () => {
  it('cancela el consumer tag inmediatamente y espera al job en curso antes de resolver', async () => {
    const channel = makeFakeChannel();
    mocks.getRabbitContext.mockReturnValue({ channel, connection: {} });

    let resolveHandler: (() => void) | undefined;
    const handler = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          resolveHandler = resolve;
        }),
    );

    const consumer = await startConsumer(handler, { inbox: fakeInbox() });

    // Simula un mensaje entrante — dispara el handler, que queda "colgado"
    // (in-flight) hasta que resolvemos manualmente mas abajo.
    channel.onMessage?.(
      fakeMessage({
        eventType: 'communication.call.recording_processing_started.v1',
        eventId: u(),
        tenantId: u(),
        callId: u(),
        recordingFileId: u(),
      }),
    );
    await flushMicrotasks();
    expect(handler).toHaveBeenCalledTimes(1);

    let stopResolved = false;
    const stopPromise = consumer.stop(5_000).then(() => {
      stopResolved = true;
    });

    // channel.cancel se llama de inmediato (deja de aceptar mensajes NUEVOS),
    // aunque el job en curso todavia no termino.
    await flushMicrotasks();
    expect(channel.cancel).toHaveBeenCalledWith('ctag-1');
    expect(stopResolved).toBe(false);

    resolveHandler?.();
    await stopPromise;
    expect(stopResolved).toBe(true);
    expect(channel.ack).toHaveBeenCalledTimes(1);
  });

  it('cuando no hay job en curso, stop() resuelve casi de inmediato', async () => {
    const channel = makeFakeChannel();
    mocks.getRabbitContext.mockReturnValue({ channel, connection: {} });
    const handler = vi.fn(async () => undefined);

    const consumer = await startConsumer(handler, { inbox: fakeInbox() });

    const started = Date.now();
    await consumer.stop(5_000);
    expect(Date.now() - started).toBeLessThan(1_000);
    expect(channel.cancel).toHaveBeenCalledWith('ctag-1');
  });
});
