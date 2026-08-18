import { describe, it, expect, vi, beforeEach, afterAll } from 'vitest';
import { randomUUID } from 'node:crypto';

/**
 * Prueba que este worker es reusable por un dominio DISTINTO de Communication
 * sin tocar codigo: configurando TRANSCRIPT_WORKER_RECORDING_KINDS con un
 * mapeo propio (aca, a modo de ejemplo, un futuro microservicio de podcasts),
 * el consumer enruta por el `eventType`/campo de id que ese mapeo declara, y
 * el publisher publica con los tipos de evento de ESE mapeo — nada de
 * 'call'/'meeting'/'callId'/'meetingId' hardcodeado en el camino.
 *
 * El override de env se hace ANTES del import dinamico de config/consumer/
 * publisher (recien se evaluan ahi) y se restaura en `afterAll`, mismo
 * patron que pipeline-retry.test.ts para no filtrar el override a otros
 * archivos de test que compartan el worker de Vitest.
 */

const ORIGINAL_RECORDING_KINDS = process.env['TRANSCRIPT_WORKER_RECORDING_KINDS'];

const CUSTOM_MAPPING = [
  {
    kind: 'podcast-episode',
    targetIdField: 'episodeId',
    triggerEventTypes: ['podcasts.episode.recording_ready.v1'],
    transcriptReadyEventType: 'podcasts.episode.transcript_ready.v1',
    transcriptFailedEventType: 'podcasts.episode.transcript_failed.v1',
  },
];

process.env['TRANSCRIPT_WORKER_RECORDING_KINDS'] = JSON.stringify(CUSTOM_MAPPING);

afterAll(() => {
  if (ORIGINAL_RECORDING_KINDS === undefined) delete process.env['TRANSCRIPT_WORKER_RECORDING_KINDS'];
  else process.env['TRANSCRIPT_WORKER_RECORDING_KINDS'] = ORIGINAL_RECORDING_KINDS;
});

const mocks = vi.hoisted(() => ({
  getRabbitContext: vi.fn(),
}));

vi.mock('../../src/rabbit/rabbit-connection.js', () => ({
  getRabbitContext: mocks.getRabbitContext,
}));

vi.resetModules();

const { startConsumer } = await import('../../src/rabbit/consumer.js');
const { publishTranscriptReady, publishTranscriptFailed } = await import('../../src/rabbit/publisher.js');

function u(): string {
  return randomUUID();
}

interface FakeChannel {
  onMessage: ((msg: unknown) => void) | undefined;
  prefetch: (n: number) => Promise<void>;
  consume: (queue: string, cb: (msg: unknown) => void) => Promise<{ consumerTag: string }>;
  publish: ReturnType<typeof vi.fn>;
  ack: ReturnType<typeof vi.fn>;
  nack: ReturnType<typeof vi.fn>;
  cancel: ReturnType<typeof vi.fn>;
}

function makeFakeChannel(): FakeChannel {
  const channel: FakeChannel = {
    onMessage: undefined,
    prefetch: async () => undefined,
    consume: async (_queue, cb) => {
      channel.onMessage = cb;
      return { consumerTag: 'ctag-generic' };
    },
    publish: vi.fn(() => true),
    ack: vi.fn(),
    nack: vi.fn(),
    cancel: vi.fn(async () => undefined),
  };
  return channel;
}

function fakeMessage(payload: Record<string, unknown>): { content: Buffer } {
  return { content: Buffer.from(JSON.stringify(payload), 'utf-8') };
}

function fakeInbox() {
  return {
    tryMarkProcessed: async () => true,
    unmark: async () => undefined,
  } as never;
}

async function flushMicrotasks(times = 5): Promise<void> {
  for (let i = 0; i < times; i += 1) {
    await Promise.resolve();
  }
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe('mapeo de recording kinds configurable (reusabilidad genérica del worker)', () => {
  it('el consumer enruta un dominio ajeno a Communication usando solo config.recordingKinds', async () => {
    const channel = makeFakeChannel();
    mocks.getRabbitContext.mockReturnValue({ channel, connection: {} });
    const handler = vi.fn(async () => undefined);

    await startConsumer(handler, { inbox: fakeInbox() });

    const episodeId = u();
    const eventId = u();
    const tenantId = u();
    const recordingFileId = u();
    channel.onMessage?.(
      fakeMessage({
        eventType: 'podcasts.episode.recording_ready.v1',
        eventId,
        tenantId,
        episodeId,
        recordingFileId,
      }),
    );
    await flushMicrotasks();

    expect(handler).toHaveBeenCalledTimes(1);
    expect(handler).toHaveBeenCalledWith(
      expect.objectContaining({ kind: 'podcast-episode', targetId: episodeId, eventId, tenantId, recordingFileId }),
    );
    expect(channel.ack).toHaveBeenCalledTimes(1);
  });

  it('un eventType que no pertenece a ningun mapeo configurado se ack-ea y se ignora (no rompe)', async () => {
    const channel = makeFakeChannel();
    mocks.getRabbitContext.mockReturnValue({ channel, connection: {} });
    const handler = vi.fn(async () => undefined);

    await startConsumer(handler, { inbox: fakeInbox() });

    channel.onMessage?.(fakeMessage({ eventType: 'communication.call.recording_ready.v1', eventId: u() }));
    await flushMicrotasks();

    expect(handler).not.toHaveBeenCalled();
    expect(channel.ack).toHaveBeenCalledTimes(1);
  });

  it('el publisher publica transcript_ready con el eventType y el campo de id del mapeo configurado', () => {
    const channel = makeFakeChannel();
    mocks.getRabbitContext.mockReturnValue({ channel, connection: {} });

    const episodeId = u();
    const tenantId = u();
    publishTranscriptReady({
      kind: 'podcast-episode',
      tenantId,
      correlationId: undefined,
      targetId: episodeId,
      recordingFileId: u(),
      transcriptFileId: u(),
      detectedLanguage: 'es',
      durationSeconds: 120,
      wordCount: 300,
    });

    expect(channel.publish).toHaveBeenCalledTimes(1);
    const [exchange, routingKey, buffer, options] = channel.publish.mock.calls[0] as [
      string,
      string,
      Buffer,
      { type: string },
    ];
    expect(exchange).toBe('taxvision-events');
    expect(routingKey).toBe('');
    expect(options.type).toBe('podcasts.episode.transcript_ready.v1');
    const body = JSON.parse(buffer.toString('utf-8')) as Record<string, unknown>;
    expect(body['eventType']).toBe('podcasts.episode.transcript_ready.v1');
    expect(body['episodeId']).toBe(episodeId);
    expect(body).not.toHaveProperty('callId');
    expect(body).not.toHaveProperty('meetingId');
  });

  it('el publisher publica transcript_failed con el eventType del mapeo configurado', () => {
    const channel = makeFakeChannel();
    mocks.getRabbitContext.mockReturnValue({ channel, connection: {} });

    const episodeId = u();
    publishTranscriptFailed({
      kind: 'podcast-episode',
      tenantId: u(),
      correlationId: undefined,
      targetId: episodeId,
      recordingFileId: u(),
      failureReason: 'WhisperError',
      errorMessage: 'whisper-cli exited with code 1',
    });

    expect(channel.publish).toHaveBeenCalledTimes(1);
    const [, , buffer, options] = channel.publish.mock.calls[0] as [string, string, Buffer, { type: string }];
    expect(options.type).toBe('podcasts.episode.transcript_failed.v1');
    const body = JSON.parse(buffer.toString('utf-8')) as Record<string, unknown>;
    expect(body['episodeId']).toBe(episodeId);
    expect(body['failureReason']).toBe('WhisperError');
  });
});
