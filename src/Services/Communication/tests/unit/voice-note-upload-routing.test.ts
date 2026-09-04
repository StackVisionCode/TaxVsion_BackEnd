import { afterEach, describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { HttpCloudStorageUploadClient } from '../../src/infrastructure/cloudstorage/http-cloudstorage-upload-client.js';
import type { ServiceTokenClient } from '../../src/infrastructure/auth/service-token-client.js';

/**
 * F1 — routing del owner en el upload: una nota de voz (contentType audio/*) va a la carpeta navegable
 * "Voice Notes" del TENANT (owner Tenant, ownerId null); cualquier otro adjunto sigue anclado a la
 * conversación (owner Communication). Se valida el body del POST a CloudStorage stubbeando fetch.
 */
function tokens(): ServiceTokenClient {
  return { async getToken() {
      return 'service-token';
    } } as unknown as ServiceTokenClient;
}

function stubFetchOk(): { bodies: Record<string, unknown>[] } {
  const bodies: Record<string, unknown>[] = [];
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string, init: { body: string }) => {
      bodies.push(JSON.parse(init.body) as Record<string, unknown>);
      return {
        ok: true,
        json: async () => ({
          fileId: randomUUID(),
          uploadUrl: 'https://minio.local/upload',
          expiresAtUtc: '2026-01-01T00:00:00.000Z',
          formData: { key: 'obj' },
        }),
      } as unknown as Response;
    }),
  );
  return { bodies };
}

afterEach(() => vi.unstubAllGlobals());

describe('voice-note upload routing (owner=Tenant)', () => {
  const client = new HttpCloudStorageUploadClient(tokens());
  const conversationId = randomUUID();

  it('routes an audio/* upload to the tenant VoiceNotes folder (ownerId null)', async () => {
    const captured = stubFetchOk();
    await client.initiate(randomUUID(), {
      originalName: 'voice_1.webm',
      contentType: 'audio/webm',
      sizeBytes: 2048,
      conversationId,
    });
    expect(captured.bodies[0]).toMatchObject({
      ownerType: 'Tenant',
      ownerId: null,
      folderType: 'VoiceNotes',
    });
  });

  it('routes an mp4 audio (Safari) the same way', async () => {
    const captured = stubFetchOk();
    await client.initiate(randomUUID(), {
      originalName: 'voice_1.mp4',
      contentType: 'audio/mp4',
      sizeBytes: 2048,
      conversationId,
    });
    expect(captured.bodies[0]).toMatchObject({ ownerType: 'Tenant', folderType: 'VoiceNotes' });
  });

  it('keeps a regular attachment anchored to the conversation (owner Communication/Other)', async () => {
    const captured = stubFetchOk();
    await client.initiate(randomUUID(), {
      originalName: 'w-2.pdf',
      contentType: 'application/pdf',
      sizeBytes: 1024,
      conversationId,
    });
    expect(captured.bodies[0]).toMatchObject({
      ownerType: 'Communication',
      ownerId: conversationId,
      folderType: 'Other',
    });
  });
});
