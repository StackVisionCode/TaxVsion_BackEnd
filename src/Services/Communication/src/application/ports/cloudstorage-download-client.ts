/**
 * Puerto M2M contra CloudStorage para emitir una URL de descarga presignada.
 * Communication es la autoridad de membresia de conversacion; ya autorizado el
 * caller, pide el presigned como actor `Service` (su token M2M ya lleva
 * `cloudstorage.file.download`). Asi un participante — staff o cliente — baja
 * un adjunto `ownerType=Communication` que el scope por-dueno de CloudStorage
 * nunca le dejaria bajar directo.
 *
 * `null` cuando CloudStorage devuelve 404 (archivo borrado); cualquier otro
 * no-2xx se propaga como excepcion (mismo criterio que el metadata client).
 */
export interface CloudStorageDownloadUrl {
  readonly downloadUrl: string;
  readonly expiresAtUtc: string;
}

export interface CloudStorageDownloadClient {
  getDownloadUrl(tenantId: string, fileId: string): Promise<CloudStorageDownloadUrl | null>;
}
