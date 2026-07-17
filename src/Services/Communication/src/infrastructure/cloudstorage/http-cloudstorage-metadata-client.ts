import type {
  CloudStorageFileMetadata,
  CloudStorageMetadataClient,
} from '../../application/ports/cloudstorage-metadata-client.js';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ServiceTokenClient } from '../auth/service-token-client.js';

/**
 * GET {cloudStorageBaseUrl}/storage/files/{fileId} → metadata. 404 = null,
 * cualquier otro no-2xx = throw. Fase Backend 8 (bug #245): al attach de una
 * grabacion validamos que la subida no haya sido un archivo vacio (audio
 * mudo, MediaRecorder que nunca capturo tracks) antes de mover
 * RecordingSession a Processing.
 */
interface RawCloudStorageMetadata {
  readonly fileId?: unknown;
  readonly id?: unknown;
  readonly sizeBytes?: unknown;
  readonly size?: unknown;
  readonly mimeType?: unknown;
  readonly contentType?: unknown;
  readonly originalName?: unknown;
  readonly fileName?: unknown;
}

export class HttpCloudStorageMetadataClient implements CloudStorageMetadataClient {
  constructor(private readonly tokens: ServiceTokenClient) {}

  async getMetadata(tenantId: string, fileId: string): Promise<CloudStorageFileMetadata | null> {
    const token = await this.tokens.getToken(tenantId);
    const response = await fetch(`${config.cloudStorage.baseUrl}/storage/files/${fileId}`, {
      method: 'GET',
      headers: { authorization: `Bearer ${token}` },
    });
    if (response.status === 404) return null;
    if (!response.ok) {
      const body = await response.text().catch(() => '');
      logger.error(
        { status: response.status, fileId, body: body.slice(0, 300) },
        'cloudstorage metadata request failed',
      );
      throw new Error(`CloudStorage metadata request failed with status ${response.status} for file ${fileId}`);
    }
    const raw = (await response.json()) as RawCloudStorageMetadata;
    // Aceptamos las 2 variantes de key names (camelCase Node y PascalCase que
    // .NET puede emitir con la config default). El resto opcional queda null.
    const rawSize = typeof raw.sizeBytes === 'number' ? raw.sizeBytes : typeof raw.size === 'number' ? raw.size : null;
    const rawFileId = typeof raw.fileId === 'string' ? raw.fileId : typeof raw.id === 'string' ? raw.id : fileId;
    if (rawSize === null) {
      throw new Error(`CloudStorage metadata for file ${fileId} did not include size.`);
    }
    return {
      fileId: rawFileId,
      sizeBytes: rawSize,
      mimeType: typeof raw.mimeType === 'string' ? raw.mimeType : typeof raw.contentType === 'string' ? raw.contentType : null,
      originalName:
        typeof raw.originalName === 'string' ? raw.originalName : typeof raw.fileName === 'string' ? raw.fileName : null,
    };
  }
}
