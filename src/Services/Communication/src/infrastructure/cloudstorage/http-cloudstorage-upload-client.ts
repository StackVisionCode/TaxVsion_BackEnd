import type {
  CloudStorageInitiatedUpload,
  CloudStorageUploadClient,
  CloudStorageUploadRequest,
} from '../../application/ports/cloudstorage-upload-client.js';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ServiceTokenClient } from '../auth/service-token-client.js';

/**
 * Inicia/finaliza la subida de un adjunto de chat en CloudStorage como actor
 * `Service`, forzando `ownerType=Communication` + `folderType=Other` (el cliente
 * no elige dueno). El token de servicio ya lleva `cloudstorage.file.upload`.
 */
interface RawInitiatedUpload {
  readonly fileId?: unknown;
  readonly FileId?: unknown;
  readonly uploadUrl?: unknown;
  readonly UploadUrl?: unknown;
  readonly formData?: unknown;
  readonly FormData?: unknown;
  readonly expiresAtUtc?: unknown;
  readonly ExpiresAtUtc?: unknown;
}

function asString(camel: unknown, pascal: unknown): string | null {
  if (typeof camel === 'string') return camel;
  if (typeof pascal === 'string') return pascal;
  return null;
}

export class HttpCloudStorageUploadClient implements CloudStorageUploadClient {
  constructor(private readonly tokens: ServiceTokenClient) {}

  async initiate(tenantId: string, request: CloudStorageUploadRequest): Promise<CloudStorageInitiatedUpload> {
    const token = await this.tokens.getToken(tenantId);
    const response = await fetch(`${config.cloudStorage.baseUrl}/storage/files/uploads`, {
      method: 'POST',
      headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
      body: JSON.stringify({
        originalName: request.originalName,
        contentType: request.contentType,
        sizeBytes: request.sizeBytes,
        ownerType: 'Communication',
        ownerId: null,
        folderType: 'Other',
        taxYear: null,
      }),
    });
    if (!response.ok) {
      const body = await response.text().catch(() => '');
      logger.error({ status: response.status, body: body.slice(0, 300) }, 'cloudstorage initiate-upload failed');
      throw new Error(`CloudStorage initiate-upload failed with status ${response.status}`);
    }
    const raw = (await response.json()) as RawInitiatedUpload;
    const fileId = asString(raw.fileId, raw.FileId);
    const uploadUrl = asString(raw.uploadUrl, raw.UploadUrl);
    const expiresAtUtc = asString(raw.expiresAtUtc, raw.ExpiresAtUtc);
    const formDataRaw = (raw.formData ?? raw.FormData) as unknown;
    if (fileId === null || uploadUrl === null || expiresAtUtc === null || typeof formDataRaw !== 'object' || formDataRaw === null) {
      throw new Error('CloudStorage initiate-upload response was missing required fields.');
    }
    // Los campos de la policy presignada se pasan verbatim al POST a MinIO.
    const formData: Record<string, string> = {};
    for (const [key, value] of Object.entries(formDataRaw as Record<string, unknown>)) {
      if (typeof value === 'string') formData[key] = value;
    }
    return { fileId, uploadUrl, formData, expiresAtUtc };
  }

  async complete(tenantId: string, fileId: string): Promise<{ status: string }> {
    const token = await this.tokens.getToken(tenantId);
    const response = await fetch(`${config.cloudStorage.baseUrl}/storage/files/${fileId}/complete`, {
      method: 'POST',
      headers: { authorization: `Bearer ${token}` },
    });
    if (!response.ok) {
      const body = await response.text().catch(() => '');
      logger.error({ status: response.status, fileId, body: body.slice(0, 300) }, 'cloudstorage complete-upload failed');
      throw new Error(`CloudStorage complete-upload failed with status ${response.status} for file ${fileId}`);
    }
    const raw = (await response.json().catch(() => ({}))) as { status?: unknown; Status?: unknown };
    return { status: asString(raw.status, raw.Status) ?? 'PendingScan' };
  }
}
