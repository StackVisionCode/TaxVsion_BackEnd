import { createWriteStream } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import type { ServiceTokenClient } from '../auth/service-token-client.js';
import { config } from '../config.js';

/**
 * Cliente CloudStorage — replica exacta de `SignatureCloudStorageClient.cs`
 * (mismos endpoints, mismo flujo en 2 pasos: initiate contra CloudStorage +
 * POST directo al presigned URL de MinIO, luego complete). NUNCA le pega a
 * MinIO directo, siempre via CloudStorage.
 *
 * OwnerType/FolderType usados aca ("Communication"/"Recordings") ya existen
 * en `FileEnums.cs` de CloudStorage, purpose-built para este caso — no un
 * fallback generico "Other" (ver auditoria previa a esta fase).
 */
interface DownloadUrlResponse {
  readonly downloadUrl: string;
}

interface InitiateUploadResponse {
  readonly fileId: string;
  readonly uploadUrl: string;
  readonly formData: Record<string, string>;
  readonly expiresAtUtc: string;
  readonly status: string;
}

export class CloudStorageClient {
  constructor(private readonly tokens: ServiceTokenClient) {}

  async downloadFile(tenantId: string, fileId: string, destPath: string): Promise<void> {
    const token = await this.tokens.getToken(tenantId);
    const initiate = await fetch(`${config.cloudStorage.baseUrl}/storage/files/${fileId}/download-url`, {
      method: 'POST',
      headers: { authorization: `Bearer ${token}` },
    });
    if (!initiate.ok) {
      throw new Error(`download-url request failed with status ${initiate.status} for file ${fileId}`);
    }
    const { downloadUrl } = (await initiate.json()) as DownloadUrlResponse;

    // El GET al presigned URL de MinIO NO lleva Authorization — la firma va
    // en la propia URL (mismo criterio que SignatureCloudStorageClient).
    const fileResponse = await fetch(downloadUrl);
    if (!fileResponse.ok || !fileResponse.body) {
      throw new Error(`presigned download failed with status ${fileResponse.status} for file ${fileId}`);
    }
    await pipeline(Readable.fromWeb(fileResponse.body as never), createWriteStream(destPath));
  }

  async uploadFile(input: {
    tenantId: string;
    filePath: string;
    originalName: string;
    contentType: string;
    sizeBytes: number;
    ownerId: string | null;
  }): Promise<{ fileId: string }> {
    const token = await this.tokens.getToken(input.tenantId);

    const initiateResponse = await fetch(`${config.cloudStorage.baseUrl}/storage/files/uploads`, {
      method: 'POST',
      headers: { authorization: `Bearer ${token}`, 'content-type': 'application/json' },
      body: JSON.stringify({
        originalName: input.originalName,
        contentType: input.contentType,
        sizeBytes: input.sizeBytes,
        ownerType: 'Communication',
        ownerId: input.ownerId,
        folderType: 'Recordings',
        taxYear: null,
      }),
    });
    if (!initiateResponse.ok) {
      throw new Error(`upload initiate failed with status ${initiateResponse.status}`);
    }
    const initiate = (await initiateResponse.json()) as InitiateUploadResponse;

    const fileBuffer = await readFile(input.filePath);
    const form = new FormData();
    for (const [key, value] of Object.entries(initiate.formData)) {
      form.append(key, value);
    }
    form.append('file', new Blob([fileBuffer], { type: input.contentType }), input.originalName);

    const uploadResponse = await fetch(initiate.uploadUrl, { method: 'POST', body: form });
    if (!uploadResponse.ok) {
      throw new Error(`presigned upload failed with status ${uploadResponse.status} for file ${initiate.fileId}`);
    }

    const completeResponse = await fetch(
      `${config.cloudStorage.baseUrl}/storage/files/${initiate.fileId}/complete`,
      { method: 'POST', headers: { authorization: `Bearer ${token}` } },
    );
    if (!completeResponse.ok) {
      throw new Error(`upload complete failed with status ${completeResponse.status} for file ${initiate.fileId}`);
    }

    return { fileId: initiate.fileId };
  }
}
