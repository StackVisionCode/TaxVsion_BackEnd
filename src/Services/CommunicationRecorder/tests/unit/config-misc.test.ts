import { describe, expect, it } from 'vitest';
import { parseProbe } from '../../src/compose/ffmpeg-runner.js';
import { loadConfig } from '../../src/config.js';
import { composedFileName, trackFileName } from '../../src/contracts/events.js';
import { createSessionSchema, openTrackSchema } from '../../src/contracts/api.js';
import { isTransientError, withRetry } from '../../src/upload/retry.js';

const required = {
  RECORDER_RABBITMQ_URI: 'amqp://localhost:5672',
  RECORDER_REDIS_URI: 'redis://localhost:6379',
  RECORDER_MINIO_ENDPOINT: 'localhost',
  RECORDER_MINIO_ACCESS_KEY: 'k',
  RECORDER_MINIO_SECRET_KEY: 's',
  RECORDER_AUTH_JWKS_URI: 'http://localhost:5124/auth/.well-known/jwks.json',
};

describe('config', () => {
  it('defaults del contrato', () => {
    const c = loadConfig(required);
    expect(c.http.port).toBe(5360);
    expect(c.rabbitmq).toEqual({ uri: 'amqp://localhost:5672', exchange: 'taxvision-events', externalUploadsQueue: 'cloudstorage-external-uploads' });
    expect(c.minio).toMatchObject({ port: 9000, useSSL: false, tempBucket: 'taxvision-temp', sourcePrefix: 'recording' });
    expect(c.auth).toMatchObject({ issuer: 'TaxVision.Auth', audience: 'TaxVision.Services', allowedClientIds: ['communication-worker'] });
    expect(c.rtp).toEqual({ listenIp: '127.0.0.1', announcedIp: null, portMin: 44_000, portMax: 44_999 });
    expect(c.maxActiveSessions).toBe(4);
    expect(c.compose).toMatchObject({ threads: 2, nice: 10 });
    expect(c.orphanTimeoutMinutes).toBe(30);
    expect(c.retry.backoffMs).toEqual([1000, 5000, 30_000]);
  });

  it('USE_SSL compara el texto, listas separadas por coma, prefijo sin barras', () => {
    const c = loadConfig({
      ...required,
      RECORDER_MINIO_USE_SSL: 'false',
      RECORDER_ALLOWED_CLIENT_IDS: 'communication-worker, communication-api ,',
      RECORDER_MINIO_SOURCE_PREFIX: '/transcript/recording/',
      RECORDER_RTP_ANNOUNCED_IP: ' 10.1.2.3 ',
    });
    expect(c.minio.useSSL).toBe(false);
    expect(loadConfig({ ...required, RECORDER_MINIO_USE_SSL: 'true' }).minio.useSSL).toBe(true);
    expect(c.auth.allowedClientIds).toEqual(['communication-worker', 'communication-api']);
    expect(c.minio.sourcePrefix).toBe('transcript/recording');
    expect(c.rtp.announcedIp).toBe('10.1.2.3');
  });

  it('falla al arrancar si falta algo o el rango RTP es inválido', () => {
    expect(() => loadConfig({ ...required, RECORDER_REDIS_URI: undefined })).toThrow();
    expect(() => loadConfig({ ...required, RECORDER_RTP_PORT_MIN: '45000', RECORDER_RTP_PORT_MAX: '44000' })).toThrow();
  });
});

describe('contratos y utilidades', () => {
  it('ffprobe: duración del contenedor o del stream más largo', () => {
    expect(parseProbe('{"format":{"duration":"12.345"},"streams":[{"codec_type":"audio"}]}')).toEqual({ durationMs: 12_345, streams: [{ codecType: 'audio' }] });
    expect(parseProbe('{"format":{},"streams":[{"codec_type":"video","duration":"3.5"}]}').durationMs).toBe(3500);
    expect(parseProbe('{"format":{"duration":"N/A"},"streams":[{"codec_type":"audio","duration":"N/A"}]}').durationMs).toBeNull();
  });

  it('nombres de archivo del contrato §9.2', () => {
    expect(composedFileName('Call', '2026-10-30T23:30:00-05:00', 'webm')).toBe('Call_Recording_2026-10-31.webm');
    expect(trackFileName('u1', 't1')).toBe('Track_u1_t1.webm');
  });

  it('validación de cuerpos', () => {
    expect(createSessionSchema.safeParse({}).success).toBe(false);
    const track = {
      trackId: 't',
      producerId: 'p',
      userId: 'u',
      kind: 'audio',
      source: 'camera',
      codec: { mimeType: 'audio/opus', payloadType: 100, clockRate: 48000 },
      ssrc: 1,
    };
    expect(openTrackSchema.safeParse(track).success).toBe(true);
    expect(openTrackSchema.safeParse({ ...track, codec: { ...track.codec, mimeType: 'video/H264' }, kind: 'video' }).success).toBe(false);
    expect(openTrackSchema.safeParse({ ...track, trackId: 'a/b' }).success).toBe(false);
  });

  it('reintento solo de errores transitorios', async () => {
    let calls = 0;
    const result = await withRetry(
      async () => {
        calls += 1;
        if (calls < 3) throw Object.assign(new Error('x'), { code: 'ETIMEDOUT' });
        return 'ok';
      },
      { maxAttempts: 4, backoffMs: [0], isRetriable: isTransientError, sleep: async () => undefined },
    );
    expect([result, calls]).toEqual(['ok', 3]);
    await expect(
      withRetry(async () => Promise.reject(Object.assign(new Error('denied'), { code: 'AccessDenied' })), {
        maxAttempts: 4,
        backoffMs: [0],
        isRetriable: isTransientError,
      }),
    ).rejects.toThrow('denied');
  });
});
