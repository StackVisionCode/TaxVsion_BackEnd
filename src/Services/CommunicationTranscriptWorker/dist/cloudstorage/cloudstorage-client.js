import { randomUUID } from 'node:crypto';
import { createWriteStream } from 'node:fs';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { config } from '../config.js';
import { putSourceObject } from './minio-uploader.js';
import { publishSaveFileRequested } from './save-file-requested-publisher.js';
/**
 * Fase Transcript 4 — lleva el status HTTP para que pipeline.ts pueda
 * clasificar 5xx como retriable (ver retry.ts) sin parsear el mensaje.
 */
export class DownloadStatusError extends Error {
    status;
    constructor(status, message) {
        super(message);
        this.status = status;
        this.name = 'DownloadStatusError';
    }
}
export class CloudStorageClient {
    tokens;
    constructor(tokens) {
        this.tokens = tokens;
    }
    async downloadFile(tenantId, fileId, destPath) {
        const token = await this.tokens.getToken(tenantId);
        const initiate = await fetch(`${config.cloudStorage.baseUrl}/storage/files/${fileId}/download-url`, {
            method: 'POST',
            headers: { authorization: `Bearer ${token}` },
        });
        if (!initiate.ok) {
            throw new DownloadStatusError(initiate.status, `download-url request failed with status ${initiate.status} for file ${fileId}`);
        }
        const { downloadUrl } = (await initiate.json());
        // El GET al presigned URL de MinIO NO lleva Authorization — la firma va
        // en la propia URL (mismo criterio que SignatureCloudStorageClient).
        const fileResponse = await fetch(downloadUrl);
        if (!fileResponse.ok || !fileResponse.body) {
            throw new DownloadStatusError(fileResponse.status, `presigned download failed with status ${fileResponse.status} for file ${fileId}`);
        }
        await pipeline(Readable.fromWeb(fileResponse.body), createWriteStream(destPath));
    }
    async uploadFile(input) {
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
//# sourceMappingURL=cloudstorage-client.js.map