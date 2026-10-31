/**
 * F7 (T-37): pedido de borrado de archivos a CloudStorage (retencion). CloudStorage verifica dueño y
 * carpeta antes de borrar (soft delete + purga), asi que un pedido repetido es inofensivo.
 */
export interface FileDeletionTarget {
  readonly fileId: string;
  readonly ownerType: 'Communication';
  readonly ownerId: string;
  readonly folderType: 'Recordings' | 'RecordingTracks' | 'Transcripts';
}

export interface FileDeletionRequester {
  requestDeletion(input: {
    tenantId: string;
    sourceReferenceId: string;
    reason: string;
    correlationId: string;
    files: readonly FileDeletionTarget[];
  }): Promise<void>;
}
