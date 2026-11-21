import { afterEach, describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { HttpCloudStorageUploadClient } from '../../src/infrastructure/cloudstorage/http-cloudstorage-upload-client.js';
import type { ServiceTokenClient } from '../../src/infrastructure/auth/service-token-client.js';

/**
 * La nota de voz se sube como archivo de la conversación (owner Communication) en la carpeta VoiceNotes:
 * así solo se abre por el chat, como el resto de adjuntos.
 */
function tokens(): ServiceTokenClient {
  return {
    async getToken() {
      return 'service-token';
    },
  } as unknown as ServiceTokenClient;
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

describe('voice-note upload owner', () => {
  const client = new HttpCloudStorageUploadClient(tokens());
  const conversationId = randomUUID();
  const uploadedByUserId = randomUUID();

  it.each(['audio/webm', 'audio/mp4'])(
    'uploads a %s voice note owned by the conversation',
    async (contentType) => {
      const captured = stubFetchOk();

      await client.initiate(randomUUID(), {
        originalName: 'voice_1.webm',
        contentType,
        sizeBytes: 2048,
        conversationId,
        uploadedByUserId,
      });

      expect(captured.bodies[0]).toMatchObject({
        ownerType: 'Communication',
        ownerId: conversationId,
        folderType: 'VoiceNotes',
        onBehalfOfUserId: uploadedByUserId,
      });
    },
  );
});
