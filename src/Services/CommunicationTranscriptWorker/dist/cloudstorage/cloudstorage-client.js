import { createWriteStream } from 'node:fs';
import { readFile } from 'node:fs/promises';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { config } from '../config.js';
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
            throw new Error(`download-url request failed with status ${initiate.status} for file ${fileId}`);
        }
        const { downloadUrl } = (await initiate.json());
        // El GET al presigned URL de MinIO NO lleva Authorization — la firma va
        // en la propia URL (mismo criterio que SignatureCloudStorageClient).
        const fileResponse = await fetch(downloadUrl);
        if (!fileResponse.ok || !fileResponse.body) {
            throw new Error(`presigned download failed with status ${fileResponse.status} for file ${fileId}`);
        }
        await pipeline(Readable.fromWeb(fileResponse.body), createWriteStream(destPath));
    }
    async uploadFile(input) {
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
        const initiate = (await initiateResponse.json());
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
        const completeResponse = await fetch(`${config.cloudStorage.baseUrl}/storage/files/${initiate.fileId}/complete`, { method: 'POST', headers: { authorization: `Bearer ${token}` } });
        if (!completeResponse.ok) {
            throw new Error(`upload complete failed with status ${completeResponse.status} for file ${initiate.fileId}`);
        }
        return { fileId: initiate.fileId };
    }
}
//# sourceMappingURL=cloudstorage-client.js.map