import type {
  CloudStorageDownloadClient,
  CloudStorageDownloadUrl,
} from '../../application/ports/cloudstorage-download-client.js';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ServiceTokenClient } from '../auth/service-token-client.js';

/**
 * POST {cloudStorageBaseUrl}/storage/files/{fileId}/download-url → presigned.
 * 404 = null (archivo borrado), cualquier otro no-2xx = throw. El token de
 * servicio ya lleva `cloudstorage.file.download` (ver ServiceAuth communication-worker).
 */
interface RawDownloadUrl {
  readonly downloadUrl?: unknown;
  readonly DownloadUrl?: unknown;
  readonly expiresAtUtc?: unknown;
  readonly ExpiresAtUtc?: unknown;
}

export class HttpCloudStorageDownloadClient implements CloudStorageDownloadClient {
  constructor(private readonly tokens: ServiceTokenClient) {}

  async getDownloadUrl(tenantId: string, fileId: string): Promise<CloudStorageDownloadUrl | null> {
    const token = await this.tokens.getToken(tenantId);
    const response = await fetch(`${config.cloudStorage.baseUrl}/storage/files/${fileId}/download-url`, {
      method: 'POST',
      headers: { authorization: `Bearer ${token}` },
    });
    if (response.status === 404) return null;
    if (!response.ok) {
      const body = await response.text().catch(() => '');
      logger.error(
        { status: response.status, fileId, body: body.slice(0, 300) },
        'cloudstorage download-url request failed',
      );
      throw new Error(`CloudStorage download-url request failed with status ${response.status} for file ${fileId}`);
    }
    // Aceptamos camelCase (Node) y PascalCase (.NET default), igual que el metadata client.
    const raw = (await response.json()) as RawDownloadUrl;
    const downloadUrl =
      typeof raw.downloadUrl === 'string' ? raw.downloadUrl : typeof raw.DownloadUrl === 'string' ? raw.DownloadUrl : null;
    const expiresAtUtc =
      typeof raw.expiresAtUtc === 'string' ? raw.expiresAtUtc : typeof raw.ExpiresAtUtc === 'string' ? raw.ExpiresAtUtc : null;
    if (downloadUrl === null || expiresAtUtc === null) {
      throw new Error(`CloudStorage download-url for file ${fileId} was missing url/expiry.`);
    }
    return { downloadUrl, expiresAtUtc };
  }
}
