import { z } from 'zod';

/**
 * F7 (contrato 9.2): eventos que publica `communication-recorder` al fanout. Communication los consume
 * (recording-consumers.ts) para pasar la sesion de Composing a Processing o a Failed. Se validan con zod:
 * llegan de otro proceso y un campo mal formado no debe romper el estado de la grabacion.
 */
export const RecorderEventTypes = {
  Composed: 'communication.recording.composed.v1',
  CompositionFailed: 'communication.recording.composition_failed.v1',
} as const;

export const RecordingComposedPayloadSchema = z.object({
  sessionId: z.string().uuid(),
  scope: z.enum(['Meeting', 'Call']),
  scopeId: z.string().uuid(),
  composedFileId: z.string().uuid(),
  composedSizeBytes: z.number().nonnegative(),
  format: z.string().min(1).max(8),
  durationSeconds: z.number().nonnegative(),
  layout: z.string().optional(),
  audioTracks: z
    .array(
      z.object({
        trackId: z.string().uuid(),
        userId: z.string().uuid(),
        fileId: z.string().uuid(),
        source: z.enum(['camera', 'screen']),
        startOffsetMs: z.number().int().nonnegative(),
        durationMs: z.number().int().nonnegative(),
      }),
    )
    .default([]),
  videoTrackCount: z.number().int().nonnegative().default(0),
  composedAtUtc: z.string().datetime().optional(),
});
export type RecordingComposedPayload = z.infer<typeof RecordingComposedPayloadSchema>;

export const RecordingCompositionFailedPayloadSchema = z.object({
  sessionId: z.string().uuid(),
  scope: z.enum(['Meeting', 'Call']),
  scopeId: z.string().uuid(),
  reason: z.string().min(1).max(64),
  errorMessage: z.string().max(2000).optional().nullable(),
});
export type RecordingCompositionFailedPayload = z.infer<typeof RecordingCompositionFailedPayloadSchema>;

/**
 * F7 (T-37): pedido de borrado a CloudStorage por retencion. Va punto a punto a la cola
 * `cloudstorage-file-deletions` con el nombre .NET del contrato existente (`FilesDeletionRequestedIntegrationEvent`)
 * y cuerpo PascalCase; no es un evento del fanout.
 */
export const FILES_DELETION_REQUESTED_TYPE = 'BuildingBlocks.Messaging.CloudStorageIntegrationEvents.FilesDeletionRequestedIntegrationEvent';
export const FILES_DELETION_QUEUE = 'cloudstorage-file-deletions';
