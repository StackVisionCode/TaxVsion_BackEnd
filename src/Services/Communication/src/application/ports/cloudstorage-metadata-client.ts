/**
 * Puerto de solo lectura contra CloudStorage — Fase Backend 8.
 * `null` cuando CloudStorage devuelve 404 (file eliminado o nunca existio); el
 * caller distingue esto de un error transitorio de red/auth (que se propaga
 * como excepcion, no como `null`).
 */
export interface CloudStorageFileMetadata {
  readonly fileId: string;
  readonly sizeBytes: number;
  readonly mimeType: string | null;
  readonly originalName: string | null;
}

export interface CloudStorageMetadataClient {
  getMetadata(tenantId: string, fileId: string): Promise<CloudStorageFileMetadata | null>;
}
