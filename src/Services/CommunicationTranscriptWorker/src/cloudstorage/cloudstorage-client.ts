import { randomUUID } from 'node:crypto';
import { createWriteStream } from 'node:fs';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import type { ServiceTokenClient } from '../auth/service-token-client.js';
import { config } from '../config.js';
import { putSourceObject } from './minio-uploader.js';
import { publishSaveFileRequested } from './save-file-requested-publisher.js';

/**
 * Cliente CloudStorage.
 *
 * Fase D2 — downloadFile (bajar la grabacion original) sigue el flujo HTTP+M2M
 * presignado sin cambios: leer de cualquier FolderType requeriria un IAM de MinIO
 * mucho mas amplio que el write scoped de uploadFile, y no estaba en el scope
 * acordado (mismo criterio que Signature dejo su DownloadAsync intacto en D1).
 *
 * uploadFile YA NO llama a CloudStorage por HTTP: sube el .txt del transcript
 * directo a MinIO (credenciales propias) y publica SaveFileRequestedIntegrationEvent
 * para que CloudStorage lo registre y escanee de forma asincrona.
 */
interface DownloadUrlResponse {
  readonly downloadUrl: string;
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
    correlationId?: string | undefined;
  }): Promise<{ fileId: string }> {
    const fileId = randomUUID();
    const sourceObjectKey = `${config.minio.sourcePrefix}/${fileId}/${input.originalName}`;

    await putSourceObject(sourceObjectKey, input.filePath, input.contentType);

    publishSaveFileRequested({
      tenantId: input.tenantId,
      fileId,
      sourceObjectKey,
      ownerId: input.ownerId,
      originalName: input.originalName,
      contentType: input.contentType,
      sizeBytes: input.sizeBytes,
      correlationId: input.correlationId,
    });

    return { fileId };
  }
}
