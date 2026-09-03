/**
 * Puerto M2M para MEDIAR la subida de un adjunto de chat. Un CustomerPortal no
 * puede crear un archivo `ownerType=Communication` (su scope por-dueno solo deja
 * `ownerType=Customer`), asi que Communication inicia/finaliza la subida con su
 * token de servicio. El binario lo sube el browser directo a MinIO con la URL
 * presignada que devuelve `initiate` — Communication nunca ve los bytes.
 *
 * Simetria de la descarga (`cloudstorage-download-client`): Communication media
 * lectura Y escritura de sus blobs, con un unico modelo de dueno.
 */
export interface CloudStorageUploadRequest {
  readonly originalName: string;
  readonly contentType: string;
  readonly sizeBytes: number;
  // El blob se ancla a la conversación: CloudStorage exige un ownerId para todo
  // ownerType != Tenant (FileObject.Create), así que va el conversationId como dueño.
  readonly conversationId: string;
}

export interface CloudStorageInitiatedUpload {
  readonly fileId: string;
  readonly uploadUrl: string;
  readonly formData: Record<string, string>;
  readonly expiresAtUtc: string;
}

export interface CloudStorageUploadClient {
  initiate(tenantId: string, request: CloudStorageUploadRequest): Promise<CloudStorageInitiatedUpload>;
  complete(tenantId: string, fileId: string): Promise<{ status: string }>;
}
