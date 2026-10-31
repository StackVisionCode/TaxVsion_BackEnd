import { z } from 'zod';

/**
 * Contrato HTTP v1 (F7_CONTRACTS §9.1). sessionId y trackId terminan como nombres de carpeta/archivo
 * del spool: solo se aceptan caracteres seguros para el sistema de archivos.
 */
const safeId = z.string().regex(/^[A-Za-z0-9_-]{1,100}$/, 'must match [A-Za-z0-9_-]{1,100}');
const isoDate = z.iso.datetime({ offset: true });

export const compositionSchema = z.object({
  width: z.number().int().min(160).max(3840).refine((n) => n % 2 === 0, 'must be even'),
  height: z.number().int().min(90).max(2160).refine((n) => n % 2 === 0, 'must be even'),
  fps: z.number().int().min(1).max(60),
  videoKbps: z.number().int().min(50).max(50_000),
  audioKbps: z.number().int().min(16).max(512),
  format: z.enum(['mp4', 'webm']),
});
export type CompositionSettings = z.infer<typeof compositionSchema>;

export const createSessionSchema = z.object({
  sessionId: safeId,
  tenantId: z.string().min(1).max(100),
  scope: z.enum(['Meeting', 'Call']),
  scopeId: z.string().min(1).max(100),
  startedAtUtc: isoDate,
  maxDurationSeconds: z.number().int().min(1),
  toleranceSeconds: z.number().int().min(0).default(0),
  tracksRetentionDays: z.number().int().min(0),
  captureVideo: z.boolean(),
  composition: compositionSchema,
});
export type CreateSessionRequest = z.infer<typeof createSessionSchema>;

export const codecSchema = z.object({
  mimeType: z
    .string()
    .refine((m) => ['audio/opus', 'video/vp8', 'video/vp9'].includes(m.toLowerCase()), {
      message: 'only audio/opus, video/VP8 and video/VP9 can be written to WebM with -c copy',
    }),
  payloadType: z.number().int().min(0).max(127),
  clockRate: z.number().int().positive(),
  channels: z.number().int().min(1).max(8).optional(),
});
export type TrackCodec = z.infer<typeof codecSchema>;

export const openTrackSchema = z
  .object({
    trackId: safeId,
    producerId: z.string().min(1).max(100),
    userId: z.string().min(1).max(100),
    kind: z.enum(['audio', 'video']),
    source: z.enum(['camera', 'screen']),
    codec: codecSchema,
    ssrc: z.number().int().min(0).max(4_294_967_295),
  })
  .refine((t) => t.codec.mimeType.toLowerCase().startsWith(`${t.kind}/`), {
    message: 'codec.mimeType does not match kind',
    path: ['codec', 'mimeType'],
  });
export type OpenTrackRequest = z.infer<typeof openTrackSchema>;

export const timelineParticipantSchema = z.object({
  userId: z.string().min(1),
  displayName: z.string().max(200).nullish(),
  role: z.string().nullish(),
  actorType: z.string().nullish(),
  /** Foto de perfil para el slate de cámara apagada; el recorder arma la URL con su propia config. */
  avatarFileId: z.string().uuid().nullish(),
});

export const timelineEventSchema = z.object({
  seq: z.number().int().nullish(),
  at: isoDate,
  type: z.string().min(1),
  actorUserId: z.string().nullish(),
  targetUserId: z.string().nullish(),
  payload: z.record(z.string(), z.unknown()).nullish(),
});

export const timelineSchema = z.object({
  participants: z.array(timelineParticipantSchema).default([]),
  events: z.array(timelineEventSchema).default([]),
});
export type Timeline = z.infer<typeof timelineSchema>;
export type TimelineEvent = z.infer<typeof timelineEventSchema>;
export type TimelineParticipant = z.infer<typeof timelineParticipantSchema>;

export const stopSessionSchema = z.object({
  stoppedAtUtc: isoDate,
  reason: z.string().min(1).max(64),
  timeline: timelineSchema.default({ participants: [], events: [] }),
});
export type StopSessionRequest = z.infer<typeof stopSessionSchema>;

export const recomposeSchema = z.object({
  timeline: timelineSchema,
  composition: compositionSchema,
});
export type RecomposeRequest = z.infer<typeof recomposeSchema>;

export const sessionParamsSchema = z.object({ id: safeId });
export const trackParamsSchema = z.object({ id: safeId, trackId: safeId });
