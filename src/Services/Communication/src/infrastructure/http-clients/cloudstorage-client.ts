import { config } from '../config.js';

/**
 * Cliente HTTP a CloudStorage. Replica el mismo flujo presignado que ya usan
 * Signature/Notification (SignatureCloudStorageClient.cs): initiate → POST
 * multipart directo a la URL presignada de MinIO → complete. Nunca toca MinIO
 * ni la BD de CloudStorage directamente. Autenticacion M2M via el endpoint
 * custom `POST auth/service-token` (NO es client_credentials OAuth2
 * estandar) — mismo mecanismo que Signature/Notification.
 */

interface CachedToken {
  readonly token: string;
  readonly expiresAtMs: number;
}

const tokenCache = new Map<string, CachedToken>();

async function acquireServiceToken(tenantId: string): Promise<string> {
  const cached = tokenCache.get(tenantId);
  if (cached && cached.expiresAtMs > Date.now() + 5_000) return cached.token;

  const res = await fetch(`${config.serviceAuth.authBaseUrl}/auth/service-token`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      clientId: config.serviceAuth.clientId,
      clientSecret: config.serviceAuth.clientSecret,
      tenantId,
    }),
  });
  if (!res.ok) {
    throw new Error(`CloudStorage service-token request failed (${res.status}).`);
  }
  const payload = (await res.json()) as { accessToken: string; expiresInSeconds: number };
  tokenCache.set(tenantId, {
    token: payload.accessToken,
    expiresAtMs: Date.now() + payload.expiresInSeconds * 1000,
  });
  return payload.accessToken;
}

interface InitiatedUpload {
  readonly fileId: string;
  readonly uploadUrl: string;
  readonly formData: Readonly<Record<string, string>>;
}

export interface CloudStorageUploadInput {
  readonly tenantId: string;
  readonly fileName: string;
  readonly contentType: string;
  readonly content: Buffer;
  readonly ownerType: 'Tenant' | 'Customer' | 'User' | 'Signature' | 'Invoice' | 'Communication';
  readonly ownerId?: string | null;
  readonly folderType:
    | 'Documents'
    | 'Receipts'
    | 'Invoices'
    | 'EmailIncoming'
    | 'EmailOutgoing'
    | 'Tasks'
    | 'Signatures'
    | 'Avatars'
    | 'Imports'
    | 'Recordings'
    | 'Backups'
    | 'Other';
}

/** Sube un archivo a CloudStorage y devuelve el `fileId` resultante (queda en estado PendingScan). */
export async function uploadToCloudStorage(input: CloudStorageUploadInput): Promise<string> {
  const token = await acquireServiceToken(input.tenantId);
  const base = config.serviceAuth.cloudStorageBaseUrl;

  const initRes = await fetch(`${base}/storage/files/uploads`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify({
      originalName: input.fileName,
      contentType: input.contentType,
      sizeBytes: input.content.byteLength,
      ownerType: input.ownerType,
      ownerId: input.ownerId ?? null,
      folderType: input.folderType,
      taxYear: null,
    }),
  });
  if (!initRes.ok) {
    throw new Error(`CloudStorage initiate upload failed (${initRes.status}).`);
  }
  const initiated = (await initRes.json()) as InitiatedUpload;

  // POST presignado a MinIO — orden importa: los campos de policy van ANTES
  // que `file` en el multipart (requisito de la presigned POST S3-compatible).
  const form = new FormData();
  for (const [key, value] of Object.entries(initiated.formData)) form.append(key, value);
  if (!Object.keys(initiated.formData).some((k) => k.toLowerCase() === 'content-type')) {
    form.append('Content-Type', input.contentType);
  }
  form.append('file', new Blob([input.content], { type: input.contentType }), input.fileName);

  const putRes = await fetch(initiated.uploadUrl, { method: 'POST', body: form });
  if (!putRes.ok) {
    throw new Error(`MinIO presigned upload failed (${putRes.status}).`);
  }

  const completeRes = await fetch(`${base}/storage/files/${initiated.fileId}/complete`, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` },
  });
  if (!completeRes.ok) {
    throw new Error(`CloudStorage complete upload failed (${completeRes.status}).`);
  }

  return initiated.fileId;
}
